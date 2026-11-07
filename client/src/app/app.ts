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

  protected submitSearch(event: Event, query: string): void {
    event.preventDefault();
    const q = query.trim();
    if (q.length >= 2) {
      void this.router.navigate(['/search'], { queryParams: { q } });
    }
  }
}
