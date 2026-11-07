import { ChangeDetectionStrategy, Component, computed, effect, input, signal } from '@angular/core';

const PALETTE = [
  { bg: '#1F3A4D', fg: '#EDE6D8', sun: '#F2573D' },
  { bg: '#B8402A', fg: '#F6EDDD', sun: '#131211' },
  { bg: '#D8C9A8', fg: '#1E1B18', sun: '#B8402A' },
  { bg: '#2F4A3C', fg: '#EDE6D8', sun: '#C98B2B' },
  { bg: '#C98B2B', fg: '#1E1B18', sun: '#F6EDDD' },
  { bg: '#3B2F4A', fg: '#EDE6D8', sun: '#C98B2B' },
  { bg: '#E4DED2', fg: '#8E2A1C', sun: '#1F3A4D' },
  { bg: '#5B6B73', fg: '#FFFFFF', sun: '#131211' },
];

/** Обложка тайтла; если её нет или она не загрузилась, рисует типографический постер. */
@Component({
  selector: 'app-poster',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (url() && !failed()) {
      <img [src]="url()" alt="" loading="lazy" referrerpolicy="no-referrer" (error)="failed.set(true)" />
    } @else {
      <div
        class="fallback"
        aria-hidden="true"
        [style.background]="colors().bg"
        [style.color]="colors().fg"
        [style.font-size.px]="fontSize()"
      >
        <span>{{ text() }}</span>
        @if (sun()) {
          <span class="sun" [style.background]="colors().sun"></span>
        }
      </div>
    }
  `,
  styles: `
    :host { display: block; aspect-ratio: 2 / 3; border-radius: 6px; overflow: hidden; background: var(--surface); }
    img { display: block; width: 100%; height: 100%; object-fit: cover; }
    .fallback {
      position: relative;
      width: 100%;
      height: 100%;
      padding: 12px;
      font-family: var(--font-poster);
      font-weight: 800;
      line-height: 1.12;
      writing-mode: vertical-rl;
      overflow: hidden;
    }
    .sun { position: absolute; left: 12px; bottom: 12px; width: 30%; aspect-ratio: 1 / 1; border-radius: 50%; }
  `,
})
export class Poster {
  readonly url = input<string | null>(null);
  readonly text = input.required<string>();
  readonly seed = input.required<number>();
  readonly fontSize = input(26);
  readonly sun = input(true);

  protected readonly failed = signal(false);
  protected readonly colors = computed(() => PALETTE[Math.abs(this.seed()) % PALETTE.length]);

  constructor() {
    effect(() => {
      this.url();
      this.failed.set(false);
    });
  }
}
