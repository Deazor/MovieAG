import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { catchError, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { BrowseFilters, BrowseResult, Genre, filtersToParams, hasFilters, parseFilters, parsePage } from '../../core/browse';
import { Loadable, animeRatings, loadable, plural, titleMeta } from '../../core/format';
import { SearchResult, TitleCard } from '../../core/models';
import { CatalogFilters } from '../../shared/catalog-filters';
import { Pager } from '../../shared/pager';
import { Poster } from '../../shared/poster';
import { Ratings } from '../../shared/ratings';

type KindFilter = 'all' | 'tv' | 'movie';

const MIN_QUERY_LENGTH = 2;

/**
 * Два режима. Только текст — поиск по всем источникам с озвучками, как раньше.
 * Без текста или с фильтрами — каталог Shikimori постранично: жанр, годы выхода, сортировка.
 */
@Component({
  selector: 'app-search',
  imports: [RouterLink, Poster, CatalogFilters, Pager, Ratings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search.html',
  styleUrl: './search.scss',
})
export class Search {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Параметры адреса: ?q=&genre=&from=&to=&sort=&page= */
  readonly q = input<string>('');
  readonly genre = input<string>();
  readonly from = input<string>();
  readonly to = input<string>();
  readonly sort = input<string>();
  readonly page = input<string>();

  protected readonly query = computed(() => (this.q() ?? '').trim());
  protected readonly filters = computed(() =>
    parseFilters({ genre: this.genre(), from: this.from(), to: this.to(), sort: this.sort() }),
  );
  protected readonly pageNumber = computed(() => parsePage(this.page()));

  protected readonly browseMode = computed(
    () => hasFilters(this.filters()) || this.pageNumber() > 1 || this.query().length < MIN_QUERY_LENGTH,
  );

  protected readonly genres = toSignal(this.api.animeGenres().pipe(catchError(() => of<Genre[]>([]))), {
    initialValue: [] as Genre[],
  });

  protected readonly genreName = computed(() => this.genres().find((g) => g.id === this.filters().genre)?.name ?? null);

  protected readonly heading = computed(() => {
    if (this.query()) return this.query();
    return this.genreName() ?? 'Каталог аниме';
  });

  // ── Каталог ──────────────────────────────────────────────

  private readonly browseRequest = computed(() =>
    this.browseMode() ? { q: this.query(), filters: this.filters(), page: this.pageNumber() } : null,
  );

  protected readonly browse = toSignal(
    toObservable(this.browseRequest).pipe(
      distinctUntilChanged((a, b) => JSON.stringify(a) === JSON.stringify(b)),
      switchMap((request) =>
        request
          ? loadable(this.api.browseAnime(request.q, request.filters, request.page))
          : of<Loadable<BrowseResult<TitleCard>> | null>(null),
      ),
    ),
    { initialValue: null },
  );

  // ── Поиск по источникам ──────────────────────────────────

  protected readonly kindFilter = signal<KindFilter>('all');
  protected readonly dubFilter = signal<string | null>(null);

  protected readonly kinds: { id: KindFilter; label: string }[] = [
    { id: 'all', label: 'Все' },
    { id: 'tv', label: 'Сериалы' },
    { id: 'movie', label: 'Фильмы' },
  ];

  protected readonly result = toSignal(
    toObservable(computed(() => (this.browseMode() ? '' : this.query()))).pipe(
      distinctUntilChanged(),
      tap(() => {
        this.kindFilter.set('all');
        this.dubFilter.set(null);
      }),
      switchMap((query) => (query ? loadable(this.api.search(query)) : of<Loadable<SearchResult> | null>(null))),
    ),
    { initialValue: null },
  );

  protected readonly data = computed(() => {
    const state = this.result();
    return state?.status === 'ready' ? state.data : null;
  });

  protected readonly dubOptions = computed(() => {
    const names = (this.data()?.items ?? []).flatMap((item) => item.dubs);
    return [...new Set(names)].sort((a, b) => a.localeCompare(b, 'ru'));
  });

  protected readonly visible = computed(() => {
    const kind = this.kindFilter();
    const dub = this.dubFilter();
    return (this.data()?.items ?? []).filter(
      (item) => this.matchesKind(item, kind) && (dub === null || item.dubs.includes(dub)),
    );
  });

  protected readonly countLabel = computed(() => {
    const n = this.visible().length;
    return `${n} ${plural(n, 'тайтл', 'тайтла', 'тайтлов')}`;
  });

  protected readonly meta = titleMeta;
  protected readonly ratings = animeRatings;

  protected onFiltersChange(change: Partial<BrowseFilters>): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: filtersToParams(change), queryParamsHandling: 'merge' });
  }

  private matchesKind(item: TitleCard, kind: KindFilter): boolean {
    if (kind === 'all') return true;
    return kind === 'movie' ? item.kind === 'movie' : item.kind !== 'movie';
  }
}
