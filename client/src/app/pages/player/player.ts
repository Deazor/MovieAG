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
import { catchError, combineLatest, distinctUntilChanged, forkJoin, map, of, retry, switchMap, throwError, timer } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, closestEpisode, loadable, plural, sourceStateLabel, sourceTone } from '../../core/format';
import { DubGroup, DubSource, HistoryItem, Playback, TitleDetails } from '../../core/models';
import { formatTime, parsePlayerMessage, seekCommands, shouldResume } from '../../core/player-bridge';
import { SourcesStore } from '../../core/sources.store';

const POSITION_SAVE_INTERVAL_MS = 10_000;
const SEEK_CONFIRM_TIMEOUT_MS = 5_000;
const PLAYBACK_RETRY_DELAY_MS = 1_500;
const MAX_NETWORK_RETRIES = 2;
const NETWORK_RETRY_DELAY_MS = 1_500;
/** Насколько близко к цели должен оказаться плеер, чтобы считать перемотку состоявшейся. */
const SEEK_TOLERANCE_SECONDS = 30;

interface ResumeState {
  seconds: number;
  /** pending: ждём запуска плеера; seeking: команда отправлена; manual: плеер её не выполнил. */
  status: 'pending' | 'seeking' | 'manual';
}

interface Selection {
  dubKey: string;
  sourceId: string;
  /** У одного источника может быть несколько плееров одной озвучки: их различает dubId. */
  dubId: string;
  episode: number;
}

interface PageData {
  details: TitleDetails;
  progress: HistoryItem | null;
}

@Component({
  selector: 'app-player',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './player.html',
  styleUrl: './player.scss',
})
export class Player implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly sources = inject(SourcesStore);
  private readonly destroyRef = inject(DestroyRef);

  /** Параметры адреса: /watch/:id?dub=&episode= */
  readonly id = input.required<string>();
  readonly dub = input<string>();
  readonly episode = input<string>();

  protected readonly selection = signal<Selection | null>(null);
  protected readonly notice = signal('');
  protected readonly quality = signal<string | null>(null);
  /** Описание сбоя потока для экрана ошибки; null, пока всё в порядке. */
  protected readonly streamError = signal<string | null>(null);
  private readonly retryAttempt = signal(0);
  protected readonly resume = signal<ResumeState | null>(null);
  private readonly watched = signal<ReadonlySet<number>>(new Set());

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');
  private hls: Hls | null = null;
  private lastSaved = '';
  private position = 0;
  private duration: number | null = null;
  private lastSavedPosition: number | null = null;
  private lastPositionSaveAt = 0;
  private seekTimer: ReturnType<typeof setTimeout> | null = null;
  /** Пока новый плеер не загрузился, старый ещё может присылать своё время: его не учитываем. */
  private playerReady = false;

  protected readonly page = toSignal(
    toObservable(this.id).pipe(
      switchMap((id) =>
        loadable(
          forkJoin({
            details: this.api.title(Number(id)),
            progress: this.api.progress(Number(id)).pipe(catchError(() => of(null))),
          }),
        ),
      ),
      map((state) => {
        if (state.status === 'ready') this.initSelection(state.data);
        return state;
      }),
    ),
    { initialValue: { status: 'loading' } as Loadable<PageData> },
  );

  protected readonly details = computed(() => {
    const state = this.page();
    return state.status === 'ready' ? state.data.details : null;
  });

  protected readonly currentDub = computed<DubGroup | null>(() => {
    const selection = this.selection();
    return this.details()?.dubs.find((d) => d.key === selection?.dubKey) ?? null;
  });

  protected readonly currentSource = computed<DubSource | null>(() => {
    const selection = this.selection();
    return (
      this.currentDub()?.sources.find((s) => s.sourceId === selection?.sourceId && s.dubId === selection?.dubId) ?? null
    );
  });

  private readonly playbackRequest = computed(() => {
    const source = this.currentSource();
    const selection = this.selection();
    const attempt = this.retryAttempt();
    return source && selection ? `${source.sourceId}|${source.dubId}|${selection.episode}|${attempt}` : null;
  });

  /** Другой источник с этой же серией в той же озвучке: его предлагаем при сбое. */
  protected readonly fallback = computed<DubSource | null>(() => {
    const selection = this.selection();
    const current = this.currentSource();
    const alternatives = this.currentDub()?.sources.filter(
      (s) => s !== current && selection !== null && s.episodes.includes(selection.episode),
    );
    return alternatives?.find((s) => this.sources.state(s.sourceId)?.state !== 'offline') ?? null;
  });

  protected readonly playback = toSignal(
    combineLatest([toObservable(this.id), toObservable(this.playbackRequest)]).pipe(
      distinctUntilChanged(([a1, a2], [b1, b2]) => a1 === b1 && a2 === b2),
      switchMap(([id, request]) => {
        const source = this.currentSource();
        const selection = this.selection();
        if (!request || !source || !selection) return of<Loadable<Playback> | null>(null);

        // Источник мог не ответить разово, например сразу после запуска: один повтор до показа ошибки.
        const request$ = this.api.playback(Number(id), source.sourceId, source.dubId, selection.episode).pipe(
          retry({
            count: 1,
            delay: (error: { status?: number }) =>
              (error.status ?? 0) >= 500 ? timer(PLAYBACK_RETRY_DELAY_MS) : throwError(() => error),
          }),
        );
        return loadable(request$);
      }),
    ),
    { initialValue: null },
  );

  protected readonly playbackData = computed(() => {
    const state = this.playback();
    return state?.status === 'ready' ? state.data : null;
  });

  protected readonly playbackFailed = computed(() => this.playback()?.status === 'error' || this.streamError() !== null);

  /** Отличаем «источник не ответил» от «ссылка получена, но поток не проигрывается». */
  protected readonly failureTitle = computed(() => {
    const state = this.playback();
    const source = this.currentSource()?.label ?? 'Источник';
    if (state?.status === 'error') {
      return state.httpStatus === 404 ? `В ${source} нет этой серии` : `${source} не отвечает`;
    }
    return 'Видео не загрузилось';
  });

  protected readonly iframeUrl = computed<SafeResourceUrl | null>(() => {
    const playback = this.playbackData();
    if (playback?.kind !== 'iframe' || !playback.url.startsWith('https://')) return null;
    return this.sanitizer.bypassSecurityTrustResourceUrl(playback.url);
  });

  protected readonly streamUrl = computed(() => {
    const playback = this.playbackData();
    if (playback?.kind !== 'hls') return null;
    return playback.qualities.find((q) => q.label === this.quality())?.url ?? playback.url;
  });

  protected readonly activeQuality = computed(() => {
    const playback = this.playbackData();
    return playback?.qualities.find((q) => q.label === this.quality())?.label ?? playback?.qualities[0]?.label ?? null;
  });

  /** Сетка серий: все номера до общего числа, а отсутствующие в озвучке недоступны. */
  protected readonly cells = computed(() => {
    const dub = this.currentDub();
    const selection = this.selection();
    if (!dub || !selection) return [];

    const lastInDubs = Math.max(...(this.details()?.dubs ?? []).flatMap((d) => d.episodes), 0);
    const total = Math.max(this.details()?.title.episodesTotal ?? 0, lastInDubs);
    const available = new Set(dub.episodes);
    const watched = this.watched();

    return Array.from({ length: total }, (_, i) => {
      const n = i + 1;
      const isAvailable = available.has(n);
      const isCurrent = n === selection.episode;
      const isWatched = watched.has(n);
      const state = !isAvailable ? ', нет в этой озвучке' : isCurrent ? ', сейчас' : isWatched ? ', просмотрена' : '';
      return { n, isAvailable, isCurrent, isWatched, label: `Серия ${n}${state}` };
    });
  });

  protected readonly canGoPrev = computed(() => this.neighbor(-1) !== null);
  protected readonly canGoNext = computed(() => this.neighbor(1) !== null);

  protected readonly resumeNotice = computed(() => {
    const resume = this.resume();
    if (!resume) return '';

    const time = formatTime(resume.seconds);
    switch (resume.status) {
      case 'pending':
        return `Вы остановились на ${time}. Запустите плеер: перемотаем автоматически, а если не получится, перемотайте вручную.`;
      case 'seeking':
        return `Перематываем на ${time}…`;
      case 'manual':
        return `Этот плеер не перематывается из приложения. Перемотайте на ${time} вручную.`;
    }
  });

  constructor() {
    const onMessage = (event: MessageEvent) => this.onPlayerMessage(event);
    const onPageHide = () => this.flushPosition();
    window.addEventListener('message', onMessage);
    window.addEventListener('pagehide', onPageHide);
    this.destroyRef.onDestroy(() => {
      window.removeEventListener('message', onMessage);
      window.removeEventListener('pagehide', onPageHide);
    });

    effect((onCleanup) => {
      const url = this.streamUrl();
      const element = this.video()?.nativeElement;
      if (!url || !element) return;

      let cancelled = false;
      onCleanup(() => {
        cancelled = true;
        this.destroyHls();
      });
      void this.attachStream(element, url, () => cancelled);
    });

    effect(() => {
      if (this.playbackData()) this.saveProgress();
    });
  }

  ngOnDestroy(): void {
    this.flushPosition();
    this.clearSeekTimer();
    this.destroyHls();
  }

  protected dubEpisodesLabel(dub: DubGroup): string {
    const total = this.details()?.title.episodesTotal;
    return total ? `${dub.episodes.length} из ${total}` : `${dub.episodes.length} ${plural(dub.episodes.length, 'серия', 'серии', 'серий')}`;
  }

  /** Живые плееры подписывать незачем: подпись нужна, только когда с источником что-то не так. */
  protected sourceLabel(sourceId: string): string {
    const status = this.sources.state(sourceId);
    return status && status.state !== 'online' ? sourceStateLabel(status) : '';
  }

  protected sourceDot(sourceId: string): string {
    return sourceTone(this.sources.state(sourceId));
  }

  protected pickDub(dub: DubGroup): void {
    const selection = this.selection();
    const wanted = selection?.episode ?? dub.episodes[0];
    const episode = closestEpisode(dub.episodes, wanted);
    if (episode === null) return;

    this.notice.set(
      episode === wanted
        ? ''
        : `В озвучке ${dub.name} нет серии ${wanted}. Включена серия ${episode}.`,
    );
    this.select(dub, episode);
  }

  protected pickSource(source: DubSource): void {
    const selection = this.selection();
    if (!selection) return;

    if (source.sourceId === selection.sourceId && source.dubId === selection.dubId) return;

    const episode = closestEpisode(source.episodes, selection.episode) ?? selection.episode;
    this.notice.set('');
    this.applySelection({ ...selection, sourceId: source.sourceId, dubId: source.dubId, episode });
  }

  protected pickQuality(label: string): void {
    if (label === this.activeQuality()) return;
    this.carryPosition();
    this.playerReady = false;
    this.quality.set(label);
  }

  protected restart(): void {
    this.clearSeekTimer();
    this.resume.set(null);
    this.position = 0;
  }

  protected onFrameLoaded(): void {
    this.playerReady = true;
  }

  protected onVideoLoaded(element: HTMLVideoElement): void {
    this.playerReady = true;
    const resume = this.resume();
    if (resume) {
      element.currentTime = resume.seconds;
      this.position = resume.seconds;
      this.resume.set(null);
    }
  }

  protected onVideoTime(element: HTMLVideoElement): void {
    if (!this.playerReady) return;
    if (Number.isFinite(element.duration)) this.duration = element.duration;
    this.reportPosition(element.currentTime);
  }

  protected onVideoPause(): void {
    this.flushPosition();
  }

  protected pickEpisode(episode: number): void {
    const dub = this.currentDub();
    if (!dub) return;
    this.notice.set('');
    this.select(dub, episode);
  }

  protected go(direction: 1 | -1): void {
    const episode = this.neighbor(direction);
    if (episode !== null) this.pickEpisode(episode);
  }

  protected retryPlayback(): void {
    this.carryPosition();
    this.playerReady = false;
    this.streamError.set(null);
    this.retryAttempt.update((n) => n + 1);
  }

  /** При hls.js ошибки видео разбирает он сам и пытается восстановиться, поэтому здесь только встроенный HLS. */
  protected onVideoError(element: HTMLVideoElement): void {
    if (this.hls) return;
    this.streamError.set(`MediaError ${element.error?.code ?? '?'}`);
  }

  private neighbor(direction: 1 | -1): number | null {
    const episodes = this.currentDub()?.episodes ?? [];
    const selection = this.selection();
    if (!selection) return null;
    const index = episodes.indexOf(selection.episode);
    return episodes[index + direction] ?? null;
  }

  private select(dub: DubGroup, episode: number): void {
    const selection = this.selection();
    const current = selection?.dubKey === dub.key ? selection : null;
    const source = this.chooseSource(dub, episode, (s) => s.sourceId === current?.sourceId && s.dubId === current?.dubId);
    if (!source) return;

    this.applySelection({ dubKey: dub.key, sourceId: source.sourceId, dubId: source.dubId, episode });
  }

  /** На той же серии позиция переносится в новый плеер, на другой начинаем с начала. */
  private applySelection(next: Selection): void {
    const previous = this.selection();
    if (previous && previous.episode !== next.episode) {
      this.flushPosition();
      this.clearSeekTimer();
      this.resume.set(null);
      this.position = 0;
      this.duration = null;
      this.lastSavedPosition = null;
    } else {
      this.carryPosition();
    }

    this.playerReady = false;
    this.streamError.set(null);
    this.selection.set(next);
  }

  private carryPosition(): void {
    this.flushPosition();
    this.startResume(this.resume()?.seconds ?? this.position);
  }

  private startResume(seconds: number): void {
    this.clearSeekTimer();
    this.resume.set(shouldResume(seconds, this.duration) ? { seconds, status: 'pending' } : null);
  }

  private onPlayerMessage(event: MessageEvent): void {
    const frame = this.frame()?.nativeElement;
    if (!this.playerReady || !frame || event.source !== frame.contentWindow) return;

    const message = parsePlayerMessage(event.data);
    if (message?.type === 'duration') this.duration = message.seconds;
    if (message?.type === 'time') this.reportPosition(message.seconds);
  }

  /**
   * Первое сообщение о времени означает, что реклама закончилась и идёт сама серия.
   * Пока плеер не дошёл до сохранённого места, позицию не перезаписываем: иначе она потеряется.
   */
  private reportPosition(seconds: number): void {
    const resume = this.resume();
    if (resume) {
      if (seconds < resume.seconds - SEEK_TOLERANCE_SECONDS) {
        if (resume.status === 'pending') this.requestSeek(resume.seconds);
        return;
      }
      this.clearSeekTimer();
      this.resume.set(null);
    }

    this.position = seconds;
    if (Date.now() - this.lastPositionSaveAt >= POSITION_SAVE_INTERVAL_MS) this.flushPosition();
  }

  private requestSeek(seconds: number): void {
    const frame = this.frame()?.nativeElement;
    if (!frame?.contentWindow) return;

    const origin = new URL(frame.src).origin;
    for (const command of seekCommands(seconds)) {
      frame.contentWindow.postMessage(command, origin);
    }

    this.resume.set({ seconds, status: 'seeking' });
    this.clearSeekTimer();
    this.seekTimer = setTimeout(() => this.resume.set({ seconds, status: 'manual' }), SEEK_CONFIRM_TIMEOUT_MS);
  }

  private clearSeekTimer(): void {
    if (this.seekTimer !== null) clearTimeout(this.seekTimer);
    this.seekTimer = null;
  }

  private flushPosition(): void {
    const selection = this.selection();
    const seconds = Math.floor(this.position);
    if (!selection || this.resume() || seconds === this.lastSavedPosition) return;

    this.lastSavedPosition = seconds;
    this.lastPositionSaveAt = Date.now();
    this.api
      .savePosition(Number(this.id()), {
        episode: selection.episode,
        seconds,
        durationSeconds: this.duration === null ? null : Math.floor(this.duration),
      })
      .subscribe({ error: (error) => console.warn('Не удалось сохранить позицию', error) });
  }

  /** Сохраняет текущий источник, если серия там есть; иначе берёт первый живой по приоритету. */
  private chooseSource(dub: DubGroup, episode: number, isPreferred: (source: DubSource) => boolean): DubSource | undefined {
    const withEpisode = dub.sources.filter((s) => s.episodes.includes(episode));
    return (
      withEpisode.find(isPreferred) ??
      withEpisode.find((s) => this.sources.state(s.sourceId)?.state !== 'offline') ??
      withEpisode[0]
    );
  }

  private initSelection({ details, progress }: PageData): void {
    this.watched.set(new Set(progress?.watchedEpisodes ?? []));
    this.lastSaved = '';

    const wantedDub = this.dub() ?? progress?.dubKey;
    const dub = details.dubs.find((d) => d.key === wantedDub) ?? details.dubs[0];
    if (!dub) {
      this.selection.set(null);
      return;
    }

    const wantedEpisode = Number(this.episode()) || progress?.lastEpisode || dub.episodes[0];
    const episode = closestEpisode(dub.episodes, wantedEpisode);
    if (episode === null) return;

    const preferred = progress?.dubKey === dub.key ? progress.sourceId : undefined;
    const source = this.chooseSource(dub, episode, (s) => s.sourceId === preferred);
    if (!source) return;

    if (progress?.lastEpisode === episode) {
      this.position = progress.positionSeconds;
      this.duration = progress.durationSeconds;
      this.lastSavedPosition = progress.positionSeconds;
      this.startResume(progress.positionSeconds);
    }
    this.selection.set({ dubKey: dub.key, sourceId: source.sourceId, dubId: source.dubId, episode });
  }

  private saveProgress(): void {
    const selection = this.selection();
    const dub = this.currentDub();
    if (!selection || !dub) return;

    const key = `${selection.episode}|${selection.dubKey}|${selection.sourceId}|${selection.dubId}`;
    if (key === this.lastSaved) return;

    const previous = this.lastSaved ? Number(this.lastSaved.split('|')[0]) : null;
    this.lastSaved = key;

    if (previous !== null && previous !== selection.episode) {
      this.watched.update((set) => new Set(set).add(previous));
    }

    this.api
      .saveProgress(Number(this.id()), {
        episode: selection.episode,
        dubKey: dub.key,
        dubName: dub.name,
        sourceId: selection.sourceId,
      })
      .subscribe({ error: (error) => console.warn('Не удалось сохранить прогресс', error) });
  }

  private async attachStream(element: HTMLVideoElement, url: string, isCancelled: () => boolean): Promise<void> {
    const { default: HlsLib } = await import('hls.js');
    if (isCancelled()) return;

    if (!HlsLib.isSupported()) {
      element.src = url;
      return;
    }

    const hls = new HlsLib();
    this.hls = hls;

    // Сначала пробуем восстановиться сами: сеть перезапрашиваем, декодер пересоздаём, и только потом сдаёмся.
    let networkRetries = 0;
    let mediaRecovered = false;
    hls.on(HlsLib.Events.ERROR, (_event, data) => {
      if (!data.fatal) return;

      if (data.type === HlsLib.ErrorTypes.NETWORK_ERROR && networkRetries < MAX_NETWORK_RETRIES) {
        networkRetries++;
        // startLoad не перезапрашивает упавший плейлист, поэтому загружаем поток заново с текущего места.
        if (element.currentTime > 0) this.startResume(element.currentTime);
        setTimeout(() => {
          if (this.hls === hls && !isCancelled()) hls.loadSource(url);
        }, NETWORK_RETRY_DELAY_MS * networkRetries);
        return;
      }
      if (data.type === HlsLib.ErrorTypes.MEDIA_ERROR && !mediaRecovered) {
        mediaRecovered = true;
        hls.recoverMediaError();
        return;
      }

      console.warn('Поток не воспроизводится', data);
      this.streamError.set(`${data.type} · ${data.details}`);
    });
    hls.loadSource(url);
    hls.attachMedia(element);
  }

  private destroyHls(): void {
    this.hls?.destroy();
    this.hls = null;
  }
}
