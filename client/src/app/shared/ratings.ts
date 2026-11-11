import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { formatRating } from '../core/format';
import { Rating } from '../core/models';

/** Оценки в строку: «8,66 Shikimori», «8,6 IMDb · 9,1 КП». Высокие выделены цветом. */
@Component({
  selector: 'app-ratings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (ratings().length > 0) {
      <span class="ratings">
        @for (rating of ratings(); track rating.source) {
          <span class="rating" [class.high]="rating.value >= highValue" [attr.aria-label]="'Оценка ' + rating.source + ': ' + format(rating.value)">
            <span class="value">{{ format(rating.value) }}</span>
            <span class="source">{{ rating.source }}</span>
          </span>
        }
      </span>
    }
  `,
  styles: `
    :host { display: block; }

    .ratings {
      display: flex;
      flex-wrap: wrap;
      gap: 4px 12px;
      font-size: 13px;
    }

    .rating { display: inline-flex; align-items: baseline; gap: 4px; white-space: nowrap; }
    .value { font-weight: 700; color: var(--text); }
    .source { color: var(--muted); }
    .high .value { color: var(--ok); }
  `,
})
export class Ratings {
  readonly ratings = input<Rating[]>([]);

  protected readonly highValue = 8;
  protected readonly format = formatRating;
}
