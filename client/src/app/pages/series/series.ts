import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { DomSanitizer, SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { catchError, combineLatest, forkJoin, map, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, imdbSeed, loadable, plural } from '../../core/format';
import { Playback, SeriesDetails, SeriesEpisode, SeriesProgressItem, SeriesStream, SeriesTitle } from '../../core/models';
import { Poster } from '../../shared/poster';
import { Ratings } from '../../shared/ratings';

interface PageData {
  details: SeriesDetails;
  progress: SeriesProgressItem | null;
}

interface Selection {
  stream: SeriesStream;
  season: number;
  episode: number;
}

/**
 * Страница сериала: озвучка, сезон и серия выбираются справа, плеер источника — в iframe.
 * Плееры Alloha не сообщают время, поэтому запоминается серия и озвучка, а не позиция внутри серии.
 */
@Component({
  selector: 'app-series',
  imports: [RouterLink, Poster, Ratings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './series.html',
  styleUrls: ['../player/player.scss', '../movie/movie.scss'],
})
export class Series {
  private readonly api = inject(ApiService);
  private readonly sanitizer = inject(DomSanitizer);

  /** Параметр адреса: /series/:imdbId */
  readonly imdbId = input.required<string>();

  protected readonly selection = signal<Selection | null>(null);
  protected readonly resumedFrom = signal<string | null>(null);
  private readonly attempt = signal(0);

  /** Серия, для которой уже записан прогресс: iframe может прислать load несколько раз. */
  private markedKey: string | null = null;

  protected readonly page = toSignal(
    toObservable(this.imdbId).pipe(
      switchMap((id) =>
        loadable(
          forkJoin({
            details: this.api.seriesDetails(id),
            progress: this.api.seriesProgress(id).pipe(catchError(() => of(null))),
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
    combineLatest([toObservable(this.selection), toObservable(this.attempt)]).pipe(
      switchMap(([selection]) =>
        selection
          ? loadable(
              this.api.seriesPlayback(
                this.imdbId(),
                selection.stream.sourceId,
                selection.stream.streamId,
                selection.season,
                selection.episode,
              ),
            )
          : of<Loadable<Playback> | null>(null),
      ),
    ),
    { initialValue: null },
  );

  protected readonly iframeUrl = computed<SafeResourceUrl | null>(() => {
    const state = this.playback();
    if (state?.status !== 'ready' || state.data.kind !== 'iframe' || !state.data.url.startsWith('https://')) return null;
    return this.sanitizer.bypassSecurityTrustResourceUrl(state.data.url);
  });

  /** Источник вернул поток, который эта страница не встраивает: не https или не iframe. */
  protected readonly unsupported = computed(() => this.playback()?.status === 'ready' && this.iframeUrl() === null);

  protected readonly failed = computed(() => this.playback()?.status === 'error');

  protected readonly seasons = computed(() => {
    const episodes = this.selection()?.stream.episodes ?? [];
    return [...new Set(episodes.map((e) => e.season))].sort((a, b) => a - b);
  });

  protected readonly seasonEpisodes = computed(() => {
    const selection = this.selection();
    if (!selection) return [];
    return selection.stream.episodes.filter((e) => e.season === selection.season).map((e) => e.episode);
  });

  protected readonly canGoPrev = computed(() => this.neighbor(-1) !== null);
  protected readonly canGoNext = computed(() => this.neighbor(1) !== null);

  protected readonly seed = imdbSeed;

  protected meta(series: SeriesTitle): string {
    const parts: string[] = [];
    if (series.year) parts.push(String(series.year));
    if (series.seasonsCount) parts.push(`${series.seasonsCount} ${plural(series.seasonsCount, 'сезон', 'сезона', 'сезонов')}`);
    parts.push(...series.genres);
    return parts.join(' · ');
  }

  protected imdbUrl(imdbId: string): string {
    return `https://www.imdb.com/title/${encodeURIComponent(imdbId)}/`;
  }

  protected episodesLabel(stream: SeriesStream): string {
    const n = stream.episodes.length;
    return `${n} ${plural(n, 'серия', 'серии', 'серий')}`;
  }

  protected isCurrentStream(stream: SeriesStream): boolean {
    const current = this.selection()?.stream;
    return current?.sourceId === stream.sourceId && current.streamId === stream.streamId;
  }

  /** Новая озвучка открывается на той же серии, если она там есть, иначе на первой серии того же сезона. */
  protected pickStream(stream: SeriesStream): void {
    const current = this.selection();
    if (!current || this.isCurrentStream(stream)) return;

    const same = stream.episodes.find((e) => e.season === current.season && e.episode === current.episode);
    const sameSeason = stream.episodes.find((e) => e.season === current.season);
    this.select(stream, same ?? sameSeason ?? stream.episodes[0]);
  }

  protected pickSeason(season: number): void {
    const current = this.selection();
    if (!current || current.season === season) return;
    this.select(current.stream, current.stream.episodes.find((e) => e.season === season));
  }

  protected pickEpisode(episode: number): void {
    const current = this.selection();
    if (!current || current.episode === episode) return;
    this.select(current.stream, { season: current.season, episode });
  }

  protected go(delta: number): void {
    const current = this.selection();
    const next = this.neighbor(delta);
    if (current && next) this.select(current.stream, next);
  }

  protected retry(): void {
    this.attempt.update((n) => n + 1);
  }

  /** Плееры без сообщений о времени иначе не попали бы в «Продолжить просмотр»: серия отмечается при загрузке. */
  protected onFrameLoaded(): void {
    const selection = this.selection();
    if (!selection) return;

    const key = `${selection.stream.sourceId}|${selection.stream.streamId}|${selection.season}|${selection.episode}`;
    if (key === this.markedKey) return;
    this.markedKey = key;

    this.api
      .saveSeriesProgress(this.imdbId(), {
        sourceId: selection.stream.sourceId,
        streamId: selection.stream.streamId,
        streamLabel: selection.stream.label,
        season: selection.season,
        episode: selection.episode,
        seconds: 0,
        durationSeconds: null,
      })
      .subscribe({ error: (error) => console.warn('Не удалось сохранить прогресс', error) });
  }

  /** Соседняя серия той же озвучки, с переходом между сезонами. */
  private neighbor(delta: number): SeriesEpisode | null {
    const selection = this.selection();
    if (!selection) return null;

    const episodes = selection.stream.episodes;
    const index = episodes.findIndex((e) => e.season === selection.season && e.episode === selection.episode);
    return index < 0 ? null : (episodes[index + delta] ?? null);
  }

  private select(stream: SeriesStream, episode: SeriesEpisode | undefined): void {
    if (!episode) return;
    this.resumedFrom.set(null);
    this.selection.set({ stream, season: episode.season, episode: episode.episode });
  }

  /** Открывает сохранённую серию в сохранённой озвучке, если она ещё есть у источника. */
  private init({ details, progress }: PageData): void {
    this.markedKey = null;
    this.resumedFrom.set(null);

    const saved = details.streams.find((s) => s.sourceId === progress?.sourceId && s.streamId === progress?.streamId);
    const savedEpisode = saved?.episodes.find((e) => e.season === progress?.season && e.episode === progress?.episode);

    if (saved && savedEpisode) {
      this.selection.set({ stream: saved, season: savedEpisode.season, episode: savedEpisode.episode });
      this.resumedFrom.set(`сезон ${savedEpisode.season}, серия ${savedEpisode.episode}`);
      return;
    }

    const first = details.streams.find((s) => s.episodes.length > 0);
    this.selection.set(first ? { stream: first, season: first.episodes[0].season, episode: first.episodes[0].episode } : null);
  }
}
