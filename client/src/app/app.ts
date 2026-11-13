import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';
import { SourcesStore } from './core/sources.store';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly router = inject(Router);
  private readonly store = inject(SourcesStore);

  protected readonly summary = this.store.summary;

  private readonly url = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => this.router.url),
      startWith(this.router.url),
    ),
    { requireSync: true },
  );

  /** На главной поиск находится в самой странице. */
  protected readonly showSearch = computed(() => this.url().split('?')[0] !== '/');

  protected readonly currentQuery = computed(() => {
    const tree = this.router.parseUrl(this.url());
    return tree.queryParams['q'] ?? '';
  });

  /** Раздел, в котором ищет строка в шапке: фильмы, сериалы или аниме-каталог. */
  private readonly section = computed<'/movies' | '/series' | '/search'>(() => {
    const url = this.url();
    if (/^\/movies(\/|\?|$)/.test(url)) return '/movies';
    if (/^\/series(\/|\?|$)/.test(url)) return '/series';
    return '/search';
  });

  protected readonly placeholder = computed(() => {
    switch (this.section()) {
      case '/movies':
        return 'Найти фильм или tt-номер';
      case '/series':
        return 'Найти сериал или tt-номер';
      default:
        return 'Найти аниме';
    }
  });

  /**
   * На своей странице текст добавляется к выбранным жанру, годам и сортировке, а страница сбрасывается.
   * Пустой запрос в каталоге убирает текст и оставляет фильтры.
   */
  protected submitSearch(event: Event, query: string): void {
    event.preventDefault();
    const q = query.trim();
    if (q.length === 1) return;

    const target = this.section();
    const samePage = this.url().split('?')[0] === target;
    if (!q && !samePage && target === '/search') return;

    void this.router.navigate([target], {
      queryParams: { q: q || null, page: null },
      queryParamsHandling: samePage ? 'merge' : undefined,
    });
  }
}
