import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { BrowseFilters, BrowseResult, Genre, filtersToParams, hasFilters, parseFilters, parsePage } from '../../core/browse';
import { Loadable, imdbSeed, loadable, plural } from '../../core/format';
import { MovieCard, MovieListResult, MovieProgressItem } from '../../core/models';
import { formatTime, shouldResume } from '../../core/player-bridge';
import { CatalogFilters } from '../../shared/catalog-filters';
import { Pager } from '../../shared/pager';
import { Poster } from '../../shared/poster';

/**
 * Без фильтров — новинки и поиск по источникам, как раньше.
 * С жанром, годами или сортировкой — каталог TMDB постранично.
 */
@Component({
  selector: 'app-movies',
  imports: [RouterLink, Poster, CatalogFilters, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './movies.html',
  styleUrl: './movies.scss',
})
export class Movies {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Параметры адреса: ?q=&genre=&from=&to=&sort=&page=; когда их нет, роутер передаёт undefined. */
  readonly q = input<string>();
  readonly genre = input<string>();
  readonly from = input<string>();
  readonly to = input<string>();
  readonly sort = input<string>();
  readonly page = input<string>();

  protected readonly query = computed(() => this.q()?.trim() ?? '');
  protected readonly filters = computed(() =>
    parseFilters({ genre: this.genre(), from: this.from(), to: this.to(), sort: this.sort() }),
  );
  protected readonly pageNumber = computed(() => parsePage(this.page()));
  protected readonly browseMode = computed(() => hasFilters(this.filters()) || this.pageNumber() > 1);

  /** 503 значит, что не задан токен TMDB: тогда вместо панели фильтров подсказка. */
  protected readonly genres = toSignal(loadable(this.api.movieGenres()), {
    initialValue: { status: 'loading' } as Loadable<Genre[]>,
  });

  protected readonly genreList = computed(() => {
    const state = this.genres();
    return state.status === 'ready' ? state.data : [];
  });

  protected readonly catalogMissing = computed(() => {
    const state = this.genres();
    return state.status === 'error' && state.httpStatus === 503;
  });

  protected readonly heading = computed(() => {
    if (this.query()) return this.query();
    return this.genreList().find((g) => g.id === this.filters().genre)?.name ?? 'Фильмы';
  });

  protected readonly result = toSignal(
    toObservable(computed(() => (this.browseMode() ? null : this.query()))).pipe(
      distinctUntilChanged(),
      switchMap((q) => (q === null ? of<Loadable<MovieListResult> | null>(null) : loadable(this.api.movies(q)))),
    ),
    { initialValue: { status: 'loading' } as Loadable<MovieListResult> | null },
  );

  private readonly browseRequest = computed(() =>
    this.browseMode() ? { q: this.query(), filters: this.filters(), page: this.pageNumber() } : null,
  );

  protected readonly browse = toSignal(
    toObservable(this.browseRequest).pipe(
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      switchMap((request) =>
        request
          ? loadable(this.api.browseMovies(request.q, request.filters, request.page))
          : of<Loadable<BrowseResult<MovieCard>> | null>(null),
      ),
    ),
    { initialValue: null },
  );

  protected onFiltersChange(change: Partial<BrowseFilters>): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: filtersToParams(change), queryParamsHandling: 'merge' });
  }

  protected readonly history = toSignal(loadable(this.api.movieHistory()), {
    initialValue: { status: 'loading' } as Loadable<MovieProgressItem[]>,
  });

  protected readonly seed = imdbSeed;

  protected countLabel(n: number): string {
    return `${n} ${plural(n, 'фильм', 'фильма', 'фильмов')}`;
  }

  /**
   * Время остановки, «Просмотрено» на финальных титрах или «Начали смотреть»,
   * если плеер не сообщает время (Alloha) и известно только, что фильм открывали.
   */
  protected watchState(item: MovieProgressItem): string {
    if (shouldResume(item.positionSeconds, item.durationSeconds)) return formatTime(item.positionSeconds);
    // Без длительности shouldResume отвечает только на вопрос «позиция уже не в самом начале?».
    return shouldResume(item.positionSeconds, null) ? 'Просмотрено' : 'Начали смотреть';
  }

  protected progressPercent(item: MovieProgressItem): number {
    if (!item.durationSeconds) return 0;
    return Math.min(100, Math.round((item.positionSeconds / item.durationSeconds) * 100));
  }
}
