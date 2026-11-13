import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { distinctUntilChanged, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { BrowseFilters, BrowseResult, Genre, filtersToParams, hasFilters, parseFilters, parsePage } from '../../core/browse';
import { Loadable, imdbSeed, loadable, plural } from '../../core/format';
import { MovieCard, MovieListResult, SeriesProgressItem } from '../../core/models';
import { CatalogFilters } from '../../shared/catalog-filters';
import { MediaGrid } from '../../shared/media-grid';
import { Pager } from '../../shared/pager';
import { Poster } from '../../shared/poster';

/**
 * Раздел «Сериалы» устроен как «Фильмы»: без фильтров — новинки и поиск по источникам,
 * с жанром, годами или сортировкой — каталог TMDB постранично.
 */
@Component({
  selector: 'app-series-list',
  imports: [RouterLink, Poster, CatalogFilters, Pager, MediaGrid],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './series-list.html',
  styleUrl: '../movies/movies.scss',
})
export class SeriesList {
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
  protected readonly genres = toSignal(loadable(this.api.seriesGenres()), {
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
    return this.genreList().find((g) => g.id === this.filters().genre)?.name ?? 'Сериалы';
  });

  protected readonly result = toSignal(
    toObservable(computed(() => (this.browseMode() ? null : this.query()))).pipe(
      distinctUntilChanged(),
      switchMap((q) => (q === null ? of<Loadable<MovieListResult> | null>(null) : loadable(this.api.series(q)))),
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
          ? loadable(this.api.browseSeries(request.q, request.filters, request.page))
          : of<Loadable<BrowseResult<MovieCard>> | null>(null),
      ),
    ),
    { initialValue: null },
  );

  protected readonly history = toSignal(loadable(this.api.seriesHistory()), {
    initialValue: { status: 'loading' } as Loadable<SeriesProgressItem[]>,
  });

  protected readonly seed = imdbSeed;

  protected onFiltersChange(change: Partial<BrowseFilters>): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: filtersToParams(change), queryParamsHandling: 'merge' });
  }

  protected countLabel(n: number): string {
    return `${n} ${plural(n, 'сериал', 'сериала', 'сериалов')}`;
  }

  /** «Сезон 2 · Серия 5 · LostFilm». */
  protected episodeLabel(item: SeriesProgressItem): string {
    return [`Сезон ${item.season}`, `Серия ${item.episode}`, item.streamLabel].filter(Boolean).join(' · ');
  }
}
