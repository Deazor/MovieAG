import { Injectable, computed, inject, signal } from '@angular/core';
import { ApiService } from './api.service';
import { SourceStatus } from './models';
import { sourcesSummary } from './format';

const REFRESH_INTERVAL_MS = 60_000;
/** Пока первая проверка не закончилась, спрашиваем чаще: она занимает секунды, а не минуту. */
const PENDING_REFRESH_MS = 2_000;

/** Статусы источников для шапки, плеера и экрана настроек. */
@Injectable({ providedIn: 'root' })
export class SourcesStore {
  private readonly api = inject(ApiService);
  private pendingRefresh: ReturnType<typeof setTimeout> | null = null;

  readonly statuses = signal<SourceStatus[]>([]);

  readonly summary = computed(() => sourcesSummary(this.statuses()));

  constructor() {
    this.refresh();
    setInterval(() => this.refresh(), REFRESH_INTERVAL_MS);
  }

  refresh(): void {
    this.api.sources().subscribe({
      next: (statuses) => {
        this.statuses.set(statuses);
        this.refreshSoonIfChecking(statuses);
      },
      error: () => this.statuses.set([]),
    });
  }

  private refreshSoonIfChecking(statuses: SourceStatus[]): void {
    if (this.pendingRefresh !== null || !statuses.some((s) => s.enabled && s.state === 'unknown')) return;

    this.pendingRefresh = setTimeout(() => {
      this.pendingRefresh = null;
      this.refresh();
    }, PENDING_REFRESH_MS);
  }

  state(id: string): SourceStatus | undefined {
    return this.statuses().find((s) => s.id === id);
  }
}
