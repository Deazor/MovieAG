import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { RouterLink } from '@angular/router';
import { catchError, of, switchMap } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { Loadable, kindLabel, loadable, plural } from '../../core/format';
import { DubGroup, DubKind, TitleDetails } from '../../core/models';
import { formatTime, shouldResume } from '../../core/player-bridge';
import { Poster } from '../../shared/poster';

@Component({
  selector: 'app-title-page',
  imports: [RouterLink, Poster],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './title.html',
  styleUrl: './title.scss',
})
export class TitlePage {
  private readonly api = inject(ApiService);

  /** Параметр :id из адреса. */
  readonly id = input.required<string>();

  private readonly id$ = toObservable(this.id);

  protected readonly state = toSignal(
    this.id$.pipe(switchMap((id) => loadable(this.api.title(Number(id))))),
    { initialValue: { status: 'loading' } as Loadable<TitleDetails> },
  );

  protected readonly progress = toSignal(
    this.id$.pipe(switchMap((id) => this.api.progress(Number(id)).pipe(catchError(() => of(null))))),
    { initialValue: null },
  );

  protected readonly details = computed(() => {
    const state = this.state();
    return state.status === 'ready' ? state.data : null;
  });

  protected readonly meta = computed(() => {
    const title = this.details()?.title;
    if (!title) return '';
    const parts = [title.originalName, kindLabel(title.kind), title.year?.toString()];
    if (title.kind !== 'movie' && title.episodesTotal) {
      parts.push(`${title.episodesTotal} ${plural(title.episodesTotal, 'серия', 'серии', 'серий')}`);
    }
    parts.push(title.studio);
    return parts.filter(Boolean).join(' · ');
  });

  /** Куда ведёт главная кнопка: на последнюю серию из истории, иначе на первую доступную. */
  protected readonly primaryAction = computed(() => {
    const details = this.details();
    if (!details || details.dubs.length === 0) return null;

    const progress = this.progress();
    if (progress) {
      const time = shouldResume(progress.positionSeconds, progress.durationSeconds)
        ? `, ${formatTime(progress.positionSeconds)}`
        : '';
      return {
        label: `Продолжить: серия ${progress.lastEpisode}${time}`,
        hint: `${progress.dubName}, как в прошлый раз`,
        query: { dub: progress.dubKey, episode: progress.lastEpisode },
      };
    }

    const dub = details.dubs[0];
    return { label: 'Смотреть', hint: dub.name, query: { dub: dub.key, episode: dub.episodes[0] } };
  });

  protected kindName(kind: DubKind): string {
    return kind === 'subtitles' ? 'Субтитры' : 'Озвучка';
  }

  protected episodesLabel(dub: DubGroup): string {
    const total = this.details()?.title.episodesTotal;
    return total ? `${dub.episodes.length} из ${total}` : String(dub.episodes.length);
  }

  protected sourceNames(dub: DubGroup): string {
    return [...new Set(dub.sources.map((s) => s.label))].join(', ');
  }
}
