import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { SourceStatus } from './models';
import { sourcesSummary } from './format';

const REFRESH_INTERVAL_MS = 60_000;

/** Статусы источников для шапки, плеера и экрана настроек. */
@Injectable({ providedIn: 'root' })
export class SourcesStore {
  private readonly api = inject(ApiService);

  readonly statuses = signal<SourceStatus[]>([]);

  readonly summary = computed(() => sourcesSummary(this.statuses()));

  constructor() {
    this.refresh();
    setInterval(() => this.refresh(), REFRESH_INTERVAL_MS);
  }

  refresh(): void {
    this.api.sources().subscribe({
      next: (statuses) => this.statuses.set(statuses),
      error: () => this.statuses.set([]),
    });
  }

  state(id: string): SourceStatus | undefined {
    return this.statuses().find((s) => s.id === id);
  }
}
