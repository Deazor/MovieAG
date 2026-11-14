import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { catchError, of, switchMap } from 'rxjs';
import { ApiService } from '../core/api.service';
import { LibraryKind, LibraryState, LibraryStatus } from '../core/models';

const EMPTY: LibraryState = { status: null, favorite: false };

/**
 * Кнопки библиотеки на странице тайтла. Статусы «Буду смотреть» и «Просмотрено» взаимоисключающие,
 * повторное нажатие снимает отметку; «Любимое» ставится независимо от статуса.
 * Отметка меняется сразу, а при ошибке сохранения возвращается обратно.
 */
@Component({
  selector: 'app-library-buttons',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="library" role="group" aria-label="Библиотека">
      <button type="button" class="chip" [attr.aria-pressed]="state().status === 'planned'" [disabled]="!loaded()" (click)="toggleStatus('planned')">
        Буду смотреть
      </button>
      <button type="button" class="chip" [attr.aria-pressed]="state().status === 'watched'" [disabled]="!loaded()" (click)="toggleStatus('watched')">
        Просмотрено
      </button>
      <button type="button" class="chip favorite" [attr.aria-pressed]="state().favorite" [disabled]="!loaded()" (click)="toggleFavorite()">
        <span aria-hidden="true">{{ state().favorite ? '♥' : '♡' }}</span> Любимое
      </button>
    </div>
    @if (error()) {
      <p class="error small" role="alert">{{ error() }}</p>
    }
  `,
  styles: `
    :host { display: block; }
    .library { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip:disabled { opacity: 0.6; }
    .favorite[aria-pressed='true'] { border-color: var(--accent); background: var(--accent); color: var(--on-accent); }
    .error { margin: 8px 0 0; color: var(--accent); font-size: 14px; }
  `,
})
export class LibraryButtons {
  private readonly api = inject(ApiService);
  private readonly destroyRef = inject(DestroyRef);

  readonly kind = input.required<LibraryKind>();
  readonly itemId = input.required<string>();
  readonly name = input.required<string>();
  readonly posterUrl = input<string | null>(null);
  readonly year = input<number | null>(null);

  protected readonly state = signal<LibraryState>(EMPTY);
  protected readonly loaded = signal(false);
  protected readonly error = signal<string | null>(null);

  private readonly key = computed(() => ({ kind: this.kind(), itemId: this.itemId() }));

  constructor() {
    toObservable(this.key)
      .pipe(
        switchMap(({ kind, itemId }) => {
          this.loaded.set(false);
          return this.api.libraryState(kind, itemId).pipe(catchError(() => of(EMPTY)));
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => {
        this.state.set(state);
        this.loaded.set(true);
      });
  }

  protected toggleStatus(status: LibraryStatus): void {
    const current = this.state();
    this.save({ ...current, status: current.status === status ? null : status });
  }

  protected toggleFavorite(): void {
    const current = this.state();
    this.save({ ...current, favorite: !current.favorite });
  }

  private save(next: LibraryState): void {
    const previous = this.state();
    this.state.set(next);
    this.error.set(null);

    this.api
      .saveLibrary(this.kind(), this.itemId(), { ...next, name: this.name(), posterUrl: this.posterUrl(), year: this.year() })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.state.set(previous);
          this.error.set('Не удалось сохранить отметку. Повторите попытку.');
        },
      });
  }
}
