import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { BrowseFilters, BrowseSort, EMPTY_FILTERS, Genre, MIN_YEAR, SORT_OPTIONS, hasFilters, parseYear } from '../core/browse';

/** Жанр, годы выхода и сортировка. Состояние живёт в адресе страницы, панель только сообщает об изменениях. */
@Component({
  selector: 'app-catalog-filters',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <form class="catalog-filters" aria-label="Фильтры каталога" (submit)="$event.preventDefault()">
      <label class="field">
        <span class="field-label">Жанр</span>
        <select (change)="setGenre($any($event.target).value)">
          <option value="" [selected]="filters().genre === null">Любой</option>
          @for (genre of genres(); track genre.id) {
            <option [value]="genre.id" [selected]="genre.id === filters().genre">{{ genre.name }}</option>
          }
        </select>
      </label>

      <fieldset class="field">
        <legend class="field-label">Годы выхода</legend>
        <div class="years">
          <input type="number" inputmode="numeric" aria-label="Год с" placeholder="с" [min]="minYear" [max]="maxYear"
                 [value]="filters().from ?? ''" (change)="setYear('from', $any($event.target).value)" />
          <span aria-hidden="true">–</span>
          <input type="number" inputmode="numeric" aria-label="Год по" placeholder="по" [min]="minYear" [max]="maxYear"
                 [value]="filters().to ?? ''" (change)="setYear('to', $any($event.target).value)" />
        </div>
      </fieldset>

      <label class="field">
        <span class="field-label">Сортировка</span>
        <select (change)="setSort($any($event.target).value)">
          @for (option of sorts; track option.id) {
            <option [value]="option.id" [selected]="option.id === (filters().sort ?? 'date')">{{ option.label }}</option>
          }
        </select>
      </label>

      @if (active()) {
        <button type="button" class="btn" (click)="filtersChange.emit(empty)">Сбросить</button>
      }
    </form>
  `,
  styles: `
    .catalog-filters {
      display: flex;
      flex-wrap: wrap;
      align-items: flex-end;
      gap: 16px 24px;
      padding: 16px 0;
      border-top: 1px solid var(--line);
      border-bottom: 1px solid var(--line);
    }

    .field {
      display: flex;
      flex-direction: column;
      gap: 6px;
      min-width: 0;
      margin: 0;
      padding: 0;
      border: 0;
    }

    .field-label {
      padding: 0;
      font-size: 14px;
      color: var(--muted);
    }

    .years { display: flex; align-items: center; gap: 8px; }

    select, input {
      min-height: 44px;
      padding: 0 12px;
      border: 1px solid var(--line-strong);
      border-radius: 8px;
      background: var(--surface);
      color: var(--text);
    }

    select { min-width: 200px; max-width: 100%; }
    input { width: 96px; }
  `,
})
export class CatalogFilters {
  readonly genres = input<Genre[]>([]);
  readonly filters = input.required<BrowseFilters>();

  /**
   * Только изменённые поля: страница сливает их с текущим адресом. Если отдавать все фильтры,
   * быстрый ввод второго года успевал затереть первый, ещё не дошедший из адреса до панели.
   */
  readonly filtersChange = output<Partial<BrowseFilters>>();

  protected readonly sorts = SORT_OPTIONS;
  protected readonly empty = EMPTY_FILTERS;
  protected readonly minYear = MIN_YEAR;
  protected readonly maxYear = new Date().getFullYear() + 5;
  protected readonly active = computed(() => hasFilters(this.filters()));

  protected setGenre(value: string): void {
    this.filtersChange.emit({ genre: value || null });
  }

  protected setSort(value: string): void {
    this.filtersChange.emit({ sort: value as BrowseSort });
  }

  /** Перепутанные «с» и «по» меняются местами, а не дают ошибку сервера. */
  protected setYear(field: 'from' | 'to', value: string): void {
    const year = parseYear(value);
    const other = field === 'from' ? this.filters().to : this.filters().from;
    const swapped = year !== null && other !== null && (field === 'from' ? year > other : year < other);

    this.filtersChange.emit(swapped ? { from: Math.min(year, other), to: Math.max(year, other) } : { [field]: year });
  }
}
