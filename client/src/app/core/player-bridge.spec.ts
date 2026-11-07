import { describe, expect, it } from 'vitest';
import { formatTime, parsePlayerMessage, seekCommands, shouldResume } from './player-bridge';

describe('parsePlayerMessage', () => {
  it.each([
    ['kodik_player_time_update', 12, 'time'],
    ['player_time_update', 12.5, 'time'],
    ['kodik_player_duration_update', 1470.01, 'duration'],
    ['player_duration_update', 1560, 'duration'],
  ])('распознаёт %s', (key, value, type) => {
    expect(parsePlayerMessage({ key, value })).toEqual({ type, seconds: value });
  });

  it.each([
    [null],
    ['flow_progress'],
    [{ key: 'kodik_player_play' }],
    [{ key: 'kodik_player_time_update', value: '12' }],
    [{ key: 'kodik_player_time_update', value: -1 }],
    [{ key: 'kodik_player_time_update', value: Number.NaN }],
    [{ key: 'kodik_player_time_update', value: Number.POSITIVE_INFINITY }],
    [{ key: 'unknown', value: 12 }],
  ])('отбрасывает %j', (data) => {
    expect(parsePlayerMessage(data)).toBeNull();
  });
});

describe('seekCommands', () => {
  it('содержит форматы Kodik и CVH', () => {
    expect(seekCommands(300)).toEqual([
      { key: 'kodik_player_api', value: { method: 'seek', seconds: 300 } },
      { method: 'seek', seconds: 300 },
    ]);
  });
});

describe('shouldResume', () => {
  it('продолжает с середины серии', () => {
    expect(shouldResume(754, 1440)).toBe(true);
  });

  it('продолжает, если длительность неизвестна', () => {
    expect(shouldResume(754, null)).toBe(true);
  });

  it('не продолжает в самом начале', () => {
    expect(shouldResume(5, 1440)).toBe(false);
    expect(shouldResume(0, 1440)).toBe(false);
    expect(shouldResume(null, null)).toBe(false);
  });

  it('не продолжает на финальных титрах', () => {
    expect(shouldResume(1400, 1440)).toBe(false);
  });
});

describe('formatTime', () => {
  it.each([
    [0, '0:00'],
    [9.9, '0:09'],
    [754, '12:34'],
    [3725, '1:02:05'],
    [-5, '0:00'],
  ])('%d → %s', (seconds, expected) => {
    expect(formatTime(seconds)).toBe(expected);
  });
});
