import { Params } from '@angular/router';

/** Порядок выдачи каталога; по умолчанию сначала новые. */
export type BrowseSort = 'date' | 'rating' | 'popularity';

export interface Genre {
  id: string;
  name: string;
}

/** Страница каталога: всего страниц каталоги сообщают не всегда, поэтому только признак продолжения. */
export interface BrowseResult<T> {
  items: T[];
  page: number;
  hasMore: boolean;
}

export interface BrowseFilters {
  genre: string | null;
  from: number | null;
  to: number | null;
  sort: BrowseSort | null;
}

export const EMPTY_FILTERS: BrowseFilters = { genre: null, from: null, to: null, sort: null };

export const SORT_OPTIONS: { id: BrowseSort; label: string }[] = [
  { id: 'date', label: 'Сначала новые' },
  { id: 'rating', label: 'По рейтингу' },
  { id: 'popularity', label: 'По популярности' },
];

export const MIN_YEAR = 1900;

const SORTS = new Set<string>(SORT_OPTIONS.map((o) => o.id));

/** Параметры адреса (?genre=22&from=2015&to=2020&sort=rating) приходят строками и могут быть подделаны. */
export function parseFilters(params: { genre?: string; from?: string; to?: string; sort?: string }): BrowseFilters {
  return {
    genre: params.genre && /^\d{1,10}$/.test(params.genre) ? params.genre : null,
    from: parseYear(params.from),
    to: parseYear(params.to),
    sort: params.sort && SORTS.has(params.sort) ? (params.sort as BrowseSort) : null,
  };
}

export function parseYear(value: string | number | null | undefined): number | null {
  const year = typeof value === 'number' ? value : Number.parseInt(value ?? '', 10);
  return Number.isInteger(year) && year >= MIN_YEAR && year <= new Date().getFullYear() + 5 ? year : null;
}

export function parsePage(value: string | undefined): number {
  const page = Number.parseInt(value ?? '', 10);
  return Number.isInteger(page) && page >= 1 && page <= 500 ? page : 1;
}

export function hasFilters(filters: BrowseFilters): boolean {
  return filters.genre !== null || filters.from !== null || filters.to !== null || filters.sort !== null;
}

/**
 * Для router.navigate с queryParamsHandling: 'merge': переданы только изменённые поля,
 * null убирает параметр из адреса, страница всегда сбрасывается на первую.
 */
export function filtersToParams(change: Partial<BrowseFilters>): Params {
  return { ...change, page: null };
}
