/**
 * Обмен сообщениями с плеерами в iframe. Форматы не задокументированы публично
 * и получены наблюдением за самими плеерами, поэтому результат перемотки всегда проверяется.
 */
export type PlayerEvent = { type: 'time' | 'duration'; seconds: number };

const TIME_KEYS = new Set(['kodik_player_time_update', 'player_time_update']);
const DURATION_KEYS = new Set(['kodik_player_duration_update', 'player_duration_update']);

/** Сообщение приходит от стороннего кода, поэтому принимаем только конечные неотрицательные числа. */
export function parsePlayerMessage(data: unknown): PlayerEvent | null {
  if (typeof data !== 'object' || data === null) return null;

  const { key, value } = data as { key?: unknown; value?: unknown };
  if (typeof key !== 'string' || typeof value !== 'number' || !Number.isFinite(value) || value < 0) return null;

  if (TIME_KEYS.has(key)) return { type: 'time', seconds: value };
  if (DURATION_KEYS.has(key)) return { type: 'duration', seconds: value };
  return null;
}

/** Команды перемотки в форматах Kodik и CVH; чужой формат каждый плеер просто игнорирует. */
export function seekCommands(seconds: number): unknown[] {
  return [{ key: 'kodik_player_api', value: { method: 'seek', seconds } }, { method: 'seek', seconds }];
}

const MIN_RESUME_SECONDS = 10;
const CREDITS_SECONDS = 60;

/** В самом начале серии продолжать нечего, а с финальных титров лучше начать заново. */
export function shouldResume(seconds: number | null | undefined, duration: number | null | undefined): boolean {
  if (!seconds || seconds < MIN_RESUME_SECONDS) return false;
  return !duration || seconds < duration - CREDITS_SECONDS;
}

export function formatTime(totalSeconds: number): string {
  const total = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const seconds = total % 60;
  const pad = (n: number) => String(n).padStart(2, '0');
  return hours > 0 ? `${hours}:${pad(minutes)}:${pad(seconds)}` : `${minutes}:${pad(seconds)}`;
}
