import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { distinctUntilChanged, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { parsePage } from '../../core/browse';
import { Loadable, imdbSeed, loadable } from '../../core/format';
import { LibraryItem, LibraryKind, LibraryList, LibraryPage } from '../../core/models';
import { Pager } from '../../shared/pager';
import { Poster } from '../../shared/poster';

const LISTS: { id: LibraryList; label: string; empty: string }[] = [
  { id: 'planned', label: 'Буду смотреть', empty: 'Отметьте «Буду смотреть» на странице аниме, фильма или сериала.' },
  { id: 'watched', label: 'Просмотрено', empty: 'Здесь появится то, что вы отметите как просмотренное.' },
  { id: 'favorite', label: 'Любимое', empty: 'Нажмите «Любимое» на странице тайтла, и он появится здесь.' },
];

const KINDS: { id: LibraryKind | null; label: string }[] = [
  { id: null, label: 'Всё' },
  { id: 'anime', label: 'Аниме' },
  { id: 'movie', label: 'Фильмы' },
  { id: 'series', label: 'Сериалы' },
];

const KIND_LABELS: Record<LibraryKind, string> = { anime: 'Аниме', movie: 'Фильм', series: 'Сериал' };

/** Вкладки и раздел хранятся в адресе: /library?list=watched&kind=movie&page=2. */
@Component({
  selector: 'app-library',
  imports: [RouterLink, Poster, Pager],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './library.html',
  styleUrl: './library.scss',
})
export class Library {
  private readonly api = inject(ApiService);

  readonly list = input<string>();
  readonly kind = input<string>();
  readonly page = input<string>();

  protected readonly lists = LISTS;
  protected readonly kinds = KINDS;

  protected readonly currentList = computed<LibraryList>(() => LISTS.find((l) => l.id === this.list())?.id ?? 'planned');
  protected readonly currentKind = computed<LibraryKind | null>(() => KINDS.find((k) => k.id === this.kind())?.id ?? null);
  protected readonly pageNumber = computed(() => parsePage(this.page()));

  protected readonly emptyText = computed(() => LISTS.find((l) => l.id === this.currentList())!.empty);

  protected readonly result = toSignal(
    toObservable(computed(() => ({ list: this.currentList(), kind: this.currentKind(), page: this.pageNumber() }))).pipe(
      distinctUntilChanged((a, b) => a.list === b.list && a.kind === b.kind && a.page === b.page),
      switchMap(({ list, kind, page }) => loadable(this.api.library(list, kind, page))),
    ),
    { initialValue: { status: 'loading' } as Loadable<LibraryPage> },
  );

  protected link(item: LibraryItem): unknown[] {
    switch (item.kind) {
      case 'anime':
        return ['/title', item.itemId];
      case 'movie':
        return ['/movies', item.itemId];
      default:
        return ['/series', item.itemId];
    }
  }

  protected seed(item: LibraryItem): number {
    return item.kind === 'anime' ? Number(item.itemId) || 0 : imdbSeed(item.itemId);
  }

  protected meta(item: LibraryItem): string {
    return [KIND_LABELS[item.kind], item.year?.toString()].filter(Boolean).join(' · ');
  }

  /** Смена вкладки или раздела начинает с первой страницы. */
  protected params(change: { list?: LibraryList; kind?: LibraryKind | null }): Record<string, string | null> {
    return {
      list: change.list ?? this.currentList(),
      kind: change.kind === undefined ? this.currentKind() : change.kind,
      page: null,
    };
  }
}
