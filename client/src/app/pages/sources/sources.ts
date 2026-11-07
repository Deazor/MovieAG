import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api.service';
import { sourceStateLabel, sourceTone } from '../../core/format';
import { SourceStatus } from '../../core/models';
import { SourcesStore } from '../../core/sources.store';

type SaveState = 'idle' | 'saving' | 'saved';

@Component({
  selector: 'app-sources',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './sources.html',
  styleUrl: './sources.scss',
})
export class Sources {
  private readonly api = inject(ApiService);
  private readonly store = inject(SourcesStore);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly statuses = this.store.statuses;
  protected readonly summary = this.store.summary;
  protected readonly yaml = signal('');
  protected readonly configLoaded = signal(false);
  protected readonly saveState = signal<SaveState>('idle');
  protected readonly checking = signal(false);
  protected readonly errors = signal<string[]>([]);

  protected readonly stateLabel = sourceStateLabel;
  protected readonly stateTone = sourceTone;

  protected readonly saveLabel = computed(() => {
    switch (this.saveState()) {
      case 'saving':
        return 'Применяем…';
      case 'saved':
        return 'Применено';
      default:
        return 'Применить';
    }
  });

  constructor() {
    this.reloadConfig();
    this.check();
  }

  protected edit(value: string): void {
    this.yaml.set(value);
    this.saveState.set('idle');
  }

  protected check(): void {
    this.checking.set(true);
    this.api
      .checkSources()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (statuses) => {
          this.statuses.set(statuses);
          this.checking.set(false);
        },
        error: () => {
          this.errors.set(['Не удалось проверить источники: сервер не отвечает']);
          this.checking.set(false);
        },
      });
  }

  protected toggle(source: SourceStatus): void {
    this.applyChange(this.api.setSourceEnabled(source.id, !source.enabled), () => this.reloadConfig());
  }

  protected save(): void {
    this.saveState.set('saving');
    this.applyChange(
      this.api.saveConfig(this.yaml()),
      () => this.saveState.set('saved'),
      () => this.saveState.set('idle'),
    );
  }

  private applyChange(request: Observable<SourceStatus[]>, onSuccess: () => void, onFailure?: () => void): void {
    this.errors.set([]);
    request.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (statuses) => {
        this.statuses.set(statuses);
        onSuccess();
      },
      error: (error: HttpErrorResponse) => {
        this.errors.set(error.error?.errors ?? [error.error?.detail ?? 'Не удалось применить изменения']);
        onFailure?.();
      },
    });
  }

  private reloadConfig(): void {
    this.api
      .config()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (yaml) => {
          this.yaml.set(yaml);
          this.configLoaded.set(true);
        },
        error: () => this.errors.set(['Не удалось прочитать sources.yaml']),
      });
  }
}
