import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  computed,
  effect,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import type Hls from 'hls.js';
import { catchError, combineLatest, forkJoin, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, imdbSeed, loadable, movieMeta } from '../../core/format';
import { attachHls } from '../../core/hls-stream';
import { MovieDetails, MovieProgressItem, MovieStream, Playback } from '../../core/models';
import { formatTime, parsePlayerMessage, seekCommands, shouldResume } from '../../core/player-bridge';
import { Poster } from '../../shared/poster';

const POSITION_SAVE_INTERVAL_MS = 10_000;
const SEEK_CONFIRM_TIMEOUT_MS = 5_000;
/** Насколько близко к цели должен оказаться плеер в iframe, чтобы считать перемотку состоявшейся. */
const SEEK_TOLERANCE_SECONDS = 30;

interface PageData {
  details: MovieDetails;
  progress: MovieProgressItem | null;
}

@Component({
  selector: 'app-movie',
  imports: [RouterLink, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './movie.html',
  styleUrls: ['../player/player.scss', './movie.scss'],
})
export class Movie implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly sanitizer = inject(DomSanitizer);

  /** Параметр адреса: /movies/:imdbId */
  readonly imdbId = input.required<string>();

  protected readonly stream = signal<MovieStream | null>(null);
  protected readonly streamError = signal<string | null>(null);
  /** Сохранённая позиция, на которую нужно перемотать после загрузки потока. */
  private readonly resumeAt = signal<number | null>(null);
  protected readonly resumedFrom = signal<string | null>(null);
  /** Плеер в iframe не выполнил команду перемотки: подсказываем время для ручной перемотки. */
  protected readonly manualSeekTo = signal<string | null>(null);
  private readonly attempt = signal(0);

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');
  private seekTimer: ReturnType<typeof setTimeout> | null = null;
  private seekRequested = false;
  private hls: Hls | null = null;
  private position = 0;
  private duration: number | null = null;
  private lastSavedPosition: number | null = null;
  private lastSaveAt = 0;
  private playerReady = false;

  protected readonly page = toSignal(
    toObservable(this.imdbId).pipe(
      switchMap((id) =>
        loadable(
          forkJoin({
            details: this.api.movie(id),
            progress: this.api.movieProgress(id).pipe(catchError(() => of(null))),
          }),
        ),
      ),
      map((state) => {
        if (state.status === 'ready') this.init(state.data);
        return state;
      }),
    ),
    { initialValue: { status: 'loading' } as Loadable<PageData> },
  );

  protected readonly playback = toSignal(
    combineLatest([toObservable(this.stream), toObservable(this.attempt)]).pipe(
      switchMap(([stream]) =>
        stream
          ? loadable(this.api.moviePlayback(this.imdbId(), stream.sourceId, stream.streamId))
          : of<Loadable<Playback> | null>(null),
      ),
    ),
    { initialValue: null },
  );

  private readonly playbackData = computed(() => {
    const state = this.playback();
    return state?.status === 'ready' ? state.data : null;
  });

  protected readonly streamUrl = computed(() => {
    const data = this.playbackData();
    return data?.kind === 'hls' ? data.url : null;
  });

  /** Плееры источников (Kodik) встраиваются только по https. */
  protected readonly iframeUrl = computed<SafeResourceUrl | null>(() => {
    const data = this.playbackData();
    if (data?.kind !== 'iframe' || !data.url.startsWith('https://')) return null;
    return this.sanitizer.bypassSecurityTrustResourceUrl(data.url);
  });

  protected readonly unsafeIframe = computed(() => this.playbackData()?.kind === 'iframe' && this.iframeUrl() === null);

  protected readonly failed = computed(() => this.playback()?.status === 'error' || this.streamError() !== null);

  protected readonly meta = movieMeta;
  protected readonly seed = imdbSeed;

  constructor() {
    const onPageHide = () => this.flush();
    const onMessage = (event: MessageEvent) => this.onPlayerMessage(event);
    window.addEventListener('pagehide', onPageHide);
    window.addEventListener('message', onMessage);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('pagehide', onPageHide);
      window.removeEventListener('message', onMessage);
    });

    effect((onCleanup) => {
      const url = this.streamUrl();
      const element = this.video()?.nativeElement;
      if (!url || !element) return;

      let cancelled = false;
      onCleanup(() => {
        cancelled = true;
        this.hls?.destroy();
        this.hls = null;
      });
      void this.attach(element, url, () => cancelled);
    });
  }

  ngOnDestroy(): void {
    this.flush();
    this.clearSeekTimer();
    this.hls?.destroy();
    this.hls = null;
  }

  protected imdbUrl(imdbId: string): string {
    return `https://www.imdb.com/title/${encodeURIComponent(imdbId)}/`;
  }

  protected isCurrent(item: MovieStream): boolean {
    const current = this.stream();
    return current?.sourceId === item.sourceId && current.streamId === item.streamId;
  }

  protected pick(item: MovieStream): void {
    if (this.isCurrent(item)) return;
    this.carryPosition();
    this.stream.set(item);
  }

  protected retry(): void {
    this.carryPosition();
    this.attempt.update((n) => n + 1);
  }

  protected restart(): void {
    const element = this.video()?.nativeElement;
    if (element) element.currentTime = 0;
    this.postSeek(0);
    this.position = 0;
    this.resumedFrom.set(null);
  }

  protected onLoaded(element: HTMLVideoElement): void {
    this.playerReady = true;
    const seconds = this.resumeAt();
    if (seconds === null) return;

    element.currentTime = seconds;
    this.position = seconds;
    this.resumeAt.set(null);
    this.resumedFrom.set(formatTime(seconds));
  }

  protected onTime(element: HTMLVideoElement): void {
    if (!this.playerReady) return;
    if (Number.isFinite(element.duration)) this.duration = element.duration;
    this.position = element.currentTime;
    if (Date.now() - this.lastSaveAt >= POSITION_SAVE_INTERVAL_MS) this.flush();
  }

  protected onPause(): void {
    this.flush();
  }

  protected onFrameLoaded(): void {
    this.playerReady = true;
    this.seekRequested = false;
  }

  /**
   * Плеер в iframe сообщает время через postMessage. Пока он не дошёл до сохранённого места
   * (например, ещё идёт реклама), позицию не перезаписываем, чтобы её не потерять.
   */
  private onPlayerMessage(event: MessageEvent): void {
    const frame = this.frame()?.nativeElement;
    if (!this.playerReady || !frame || event.source !== frame.contentWindow) return;

    const message = parsePlayerMessage(event.data);
    if (!message) return;
    if (message.type === 'duration') {
      this.duration = message.seconds;
      return;
    }

    const target = this.resumeAt();
    if (target !== null) {
      if (message.seconds < target - SEEK_TOLERANCE_SECONDS) {
        if (!this.seekRequested) this.requestSeek(target);
        return;
      }
      this.clearSeekTimer();
      this.resumeAt.set(null);
      this.manualSeekTo.set(null);
      this.resumedFrom.set(formatTime(target));
    }

    this.position = message.seconds;
    if (Date.now() - this.lastSaveAt >= POSITION_SAVE_INTERVAL_MS) this.flush();
  }

  private requestSeek(seconds: number): void {
    this.seekRequested = true;
    this.postSeek(seconds);
    this.clearSeekTimer();
    this.seekTimer = setTimeout(() => this.manualSeekTo.set(formatTime(seconds)), SEEK_CONFIRM_TIMEOUT_MS);
  }

  private postSeek(seconds: number): void {
    const frame = this.frame()?.nativeElement;
    if (!frame?.contentWindow) return;
    const origin = new URL(frame.src).origin;
    for (const command of seekCommands(seconds)) frame.contentWindow.postMessage(command, origin);
  }

  private clearSeekTimer(): void {
    if (this.seekTimer !== null) clearTimeout(this.seekTimer);
    this.seekTimer = null;
  }

  /** При hls.js ошибки разбирает он сам, поэтому здесь только встроенный HLS. */
  protected onError(element: HTMLVideoElement): void {
    if (!this.hls) this.streamError.set(`MediaError ${element.error?.code ?? '?'}`);
  }

  private init({ details, progress }: PageData): void {
    this.playerReady = false;
    this.streamError.set(null);
    this.resumedFrom.set(null);
    this.manualSeekTo.set(null);
    this.clearSeekTimer();
    this.position = progress?.positionSeconds ?? 0;
    this.duration = progress?.durationSeconds ?? null;
    this.lastSavedPosition = progress?.positionSeconds ?? null;
    this.resumeAt.set(shouldResume(this.position, this.duration) ? this.position : null);

    const saved = details.streams.find((s) => s.sourceId === progress?.sourceId && s.streamId === progress?.streamId);
    this.stream.set(saved ?? details.streams[0] ?? null);
  }

  /** При смене варианта или повторе продолжаем с того же места. */
  private carryPosition(): void {
    this.flush();
    this.playerReady = false;
    this.streamError.set(null);
    this.manualSeekTo.set(null);
    this.clearSeekTimer();
    this.resumeAt.set(this.resumeAt() ?? (shouldResume(this.position, this.duration) ? this.position : null));
  }

  private flush(): void {
    const stream = this.stream();
    const seconds = Math.floor(this.position);
    if (!stream || !this.playerReady || seconds === 0 || seconds === this.lastSavedPosition) return;

    this.lastSavedPosition = seconds;
    this.lastSaveAt = Date.now();
    this.api
      .saveMovieProgress(this.imdbId(), {
        sourceId: stream.sourceId,
        streamId: stream.streamId,
        seconds,
        durationSeconds: this.duration === null ? null : Math.floor(this.duration),
      })
      .subscribe({ error: (error) => console.warn('Не удалось сохранить позицию', error) });
  }

  private async attach(element: HTMLVideoElement, url: string, isCancelled: () => boolean): Promise<void> {
    const hls = await attachHls(element, url, {
      isCancelled,
      onReload: (currentTime) => {
        if (currentTime > 0) this.resumeAt.set(currentTime);
      },
      onFatal: (detail) => this.streamError.set(detail),
    });

    if (isCancelled()) hls?.destroy();
    else this.hls = hls;
  }
}
