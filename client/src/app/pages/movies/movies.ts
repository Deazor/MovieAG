import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, imdbSeed, loadable, plural } from '../../core/format';
import { MovieListResult, MovieProgressItem } from '../../core/models';
import { formatTime, shouldResume } from '../../core/player-bridge';
import { Poster } from '../../shared/poster';

@Component({
  selector: 'app-movies',
  imports: [RouterLink, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './movies.html',
  styleUrl: './movies.scss',
})
export class Movies {
  private readonly api = inject(ApiService);

  /** Параметр ?q= из адреса; когда его нет, роутер передаёт undefined. */
  readonly q = input<string>();

  protected readonly query = computed(() => this.q()?.trim() ?? '');

  protected readonly result = toSignal(toObservable(this.query).pipe(switchMap((q) => loadable(this.api.movies(q)))), {
    initialValue: { status: 'loading' } as Loadable<MovieListResult>,
  });

  protected readonly history = toSignal(loadable(this.api.movieHistory()), {
    initialValue: { status: 'loading' } as Loadable<MovieProgressItem[]>,
  });

  protected readonly seed = imdbSeed;

  protected countLabel(n: number): string {
    return `${n} ${plural(n, 'фильм', 'фильма', 'фильмов')}`;
  }

  protected stoppedAt(item: MovieProgressItem): string | null {
    return shouldResume(item.positionSeconds, item.durationSeconds) ? formatTime(item.positionSeconds) : null;
  }

  protected progressPercent(item: MovieProgressItem): number {
    if (!item.durationSeconds) return 0;
    return Math.min(100, Math.round((item.positionSeconds / item.durationSeconds) * 100));
  }
}
