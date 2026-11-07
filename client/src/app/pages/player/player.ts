import {
  ChangeDetectionStrategy,
  Component,
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
import { catchError, combineLatest, distinctUntilChanged, forkJoin, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, closestEpisode, loadable, plural, sourceStateLabel, sourceTone } from '../../core/format';
import { DubGroup, DubSource, HistoryItem, Playback, TitleDetails } from '../../core/models';
import { SourcesStore } from '../../core/sources.store';

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

  /** Параметры адреса: /watch/:id?dub=&episode= */
  readonly id = input.required<string>();
  readonly dub = input<string>();
  readonly episode = input<string>();

  protected readonly selection = signal<Selection | null>(null);
  protected readonly notice = signal('');
  protected readonly quality = signal<string | null>(null);
  protected readonly streamFailed = signal(false);
  private readonly watched = signal<ReadonlySet<number>>(new Set());

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private hls: Hls | null = null;
  private lastSaved = '';

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
    return source && selection ? `${source.sourceId}|${source.dubId}|${selection.episode}` : null;
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
        return loadable(this.api.playback(Number(id), source.sourceId, source.dubId, selection.episode));
      }),
    ),
    { initialValue: null },
  );

  protected readonly playbackData = computed(() => {
    const state = this.playback();
    return state?.status === 'ready' ? state.data : null;
  });

  protected readonly playbackFailed = computed(() => this.playback()?.status === 'error' || this.streamFailed());

  protected readonly failureTitle = computed(() => {
    const state = this.playback();
    const source = this.currentSource()?.label ?? 'Источник';
    return state?.status === 'error' && state.httpStatus === 404
      ? `В ${source} нет этой серии`
      : `${source} не отвечает`;
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

  constructor() {
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

    const episode = closestEpisode(source.episodes, selection.episode) ?? selection.episode;
    this.notice.set('');
    this.streamFailed.set(false);
    this.selection.set({ ...selection, sourceId: source.sourceId, dubId: source.dubId, episode });
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

  protected onStreamError(): void {
    this.streamFailed.set(true);
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

    this.streamFailed.set(false);
    this.selection.set({ dubKey: dub.key, sourceId: source.sourceId, dubId: source.dubId, episode });
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
    if (source) this.selection.set({ dubKey: dub.key, sourceId: source.sourceId, dubId: source.dubId, episode });
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
    hls.on(HlsLib.Events.ERROR, (_event, data) => {
      if (data.fatal) this.onStreamError();
    });
    hls.loadSource(url);
    hls.attachMedia(element);
  }

  private destroyHls(): void {
    this.hls?.destroy();
    this.hls = null;
  }
}
