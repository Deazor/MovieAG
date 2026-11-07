import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { Loadable, loadable } from '../../core/format';
import { HistoryItem, TitleCard } from '../../core/models';
import { Poster } from '../../shared/poster';

@Component({
  selector: 'app-home',
  imports: [RouterLink, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly history = toSignal(loadable(this.api.history()), {
    initialValue: { status: 'loading' } as Loadable<HistoryItem[]>,
  });

  protected readonly fresh = toSignal(loadable(this.api.fresh()), {
    initialValue: { status: 'loading' } as Loadable<TitleCard[]>,
  });

  protected submit(event: Event, query: string): void {
    event.preventDefault();
    const q = query.trim();
    if (q.length >= 2) {
      void this.router.navigate(['/search'], { queryParams: { q } });
    }
  }

  protected progressPercent(item: HistoryItem): number {
    if (!item.episodesTotal) return 0;
    return Math.min(100, Math.round((item.watchedCount / item.episodesTotal) * 100));
  }
}
