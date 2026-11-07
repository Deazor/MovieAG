import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { of, switchMap, tap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, loadable, plural, titleMeta } from '../../core/format';
import { SearchResult, TitleCard } from '../../core/models';
import { Poster } from '../../shared/poster';

type KindFilter = 'all' | 'tv' | 'movie';

const MIN_QUERY_LENGTH = 2;

@Component({
  selector: 'app-search',
  imports: [RouterLink, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './search.html',
  styleUrl: './search.scss',
})
export class Search {
  private readonly api = inject(ApiService);

  /** Параметр ?q= из адреса. */
  readonly q = input<string>('');

  protected readonly kindFilter = signal<KindFilter>('all');
  protected readonly dubFilter = signal<string | null>(null);

  protected readonly kinds: { id: KindFilter; label: string }[] = [
    { id: 'all', label: 'Все' },
    { id: 'tv', label: 'Сериалы' },
    { id: 'movie', label: 'Фильмы' },
  ];

  protected readonly result = toSignal(
    toObservable(this.q).pipe(
      tap(() => {
        this.kindFilter.set('all');
        this.dubFilter.set(null);
      }),
      switchMap((q) => {
        const query = q.trim();
        return query.length < MIN_QUERY_LENGTH ? of<Loadable<SearchResult> | null>(null) : loadable(this.api.search(query));
      }),
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

  private matchesKind(item: TitleCard, kind: KindFilter): boolean {
    if (kind === 'all') return true;
    return kind === 'movie' ? item.kind === 'movie' : item.kind !== 'movie';
  }
}
