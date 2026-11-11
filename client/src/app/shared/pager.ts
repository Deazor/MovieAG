import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/** Переход по страницам через ?page= в адресе: остальные параметры сохраняются. */
@Component({
  selector: 'app-pager',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (page() > 1 || hasMore()) {
      <nav class="pager" aria-label="Страницы">
        @if (page() > 1) {
          <a class="btn" [routerLink]="[]" [queryParams]="{ page: page() > 2 ? page() - 1 : null }" queryParamsHandling="merge" rel="prev">← Назад</a>
        }
        <span class="muted">Страница {{ page() }}</span>
        @if (hasMore()) {
          <a class="btn" [routerLink]="[]" [queryParams]="{ page: page() + 1 }" queryParamsHandling="merge" rel="next">Дальше →</a>
        }
      </nav>
    }
  `,
  styles: `
    .pager {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 16px;
      padding-top: 8px;
    }
  `,
})
export class Pager {
  readonly page = input.required<number>();
  readonly hasMore = input.required<boolean>();
}
