import { Observable, catchError, map, of, startWith } from 'rxjs';
import { SourceStatus } from './models';

export type Loadable<T> =
  | { status: 'loading' }
  | { status: 'ready'; data: T }
  | { status: 'error'; httpStatus: number | null };

/** Оборачивает запрос в состояние для шаблона: загрузка, данные или ошибка. */
export function loadable<T>(source: Observable<T>): Observable<Loadable<T>> {
  return source.pipe(
    map((data): Loadable<T> => ({ status: 'ready', data })),
    catchError((error: { status?: number }) =>
      of<Loadable<T>>({ status: 'error', httpStatus: error.status ?? null }),
    ),
    startWith<Loadable<T>>({ status: 'loading' }),
  );
}

export function plural(n: number, one: string, few: string, many: string): string {
  const tens = n % 100;
  const units = n % 10;
  if (tens >= 11 && tens <= 14) return many;
  if (units === 1) return one;
  if (units >= 2 && units <= 4) return few;
  return many;
}

const KIND_LABELS: Record<string, string> = {
  tv: 'Сериал',
  movie: 'Фильм',
  ova: 'OVA',
  ona: 'ONA',
  web: 'ONA',
  special: 'Спецвыпуск',
  tv_special: 'Спецвыпуск',
  music: 'Клип',
};

export function kindLabel(kind: string | null): string {
  return kind ? (KIND_LABELS[kind] ?? kind.toUpperCase()) : '';
}

export function titleMeta(item: { kind: string | null; year: number | null; episodesTotal: number | null }): string {
  const parts: string[] = [];
  if (item.kind) parts.push(kindLabel(item.kind));
  if (item.year) parts.push(String(item.year));
  if (item.kind !== 'movie' && item.episodesTotal) {
    parts.push(`${item.episodesTotal} ${plural(item.episodesTotal, 'серия', 'серии', 'серий')}`);
  }
  return parts.join(' · ');
}

export function sourcesSummary(statuses: SourceStatus[]): { label: string; tone: 'ok' | 'warn' | 'bad' | '' } {
  const enabled = statuses.filter((s) => s.enabled);
  if (enabled.length === 0) return { label: 'Нет включённых источников', tone: 'bad' };

  const online = enabled.filter((s) => s.state === 'online').length;
  const noun = plural(enabled.length, 'источника', 'источников', 'источников');
  const tone = online === enabled.length ? 'ok' : online === 0 ? 'bad' : 'warn';
  return { label: `${online} из ${enabled.length} ${noun} в сети`, tone };
}

export function sourceStateLabel(status: SourceStatus): string {
  switch (status.state) {
    case 'online':
      return status.latencyMs === null ? 'В сети' : `В сети · ${status.latencyMs} мс`;
    case 'offline':
      return 'Не отвечает';
    case 'disabled':
      return 'Выключен';
    case 'needsToken':
      return 'Нужен токен';
    case 'noAdapter':
      return 'Нет адаптера';
    default:
      return 'Проверяется';
  }
}

export function sourceTone(status: SourceStatus | undefined): 'ok' | 'warn' | 'bad' | '' {
  switch (status?.state) {
    case 'online':
      return 'ok';
    case 'offline':
      return 'bad';
    case 'needsToken':
    case 'noAdapter':
      return 'warn';
    default:
      return '';
  }
}

/** Серия, которую включить при смене озвучки: та же, иначе ближайшая предыдущая, иначе первая. */
export function closestEpisode(available: number[], wanted: number): number | null {
  if (available.length === 0) return null;
  if (available.includes(wanted)) return wanted;
  const previous = available.filter((e) => e < wanted);
  return previous.length > 0 ? previous[previous.length - 1] : available[0];
}
