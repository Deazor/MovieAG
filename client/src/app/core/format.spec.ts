import { describe, expect, it } from 'vitest';
import { closestEpisode, imdbSeed, movieMeta, plural, sourcesSummary, titleMeta } from './format';
import { SourceStatus } from './models';

describe('imdbSeed', () => {
  it('берёт числовую часть IMDb ID', () => {
    expect(imdbSeed('tt1254207')).toBe(1254207);
  });

  it('не падает на некорректном значении', () => {
    expect(imdbSeed('xx')).toBe(0);
  });
});

describe('movieMeta', () => {
  it('собирает год, длительность и жанры', () => {
    expect(movieMeta({ year: 2008, runtimeMinutes: 10, genres: ['Мультфильм', 'Комедия'] })).toBe(
      '2008 · 10 мин · Мультфильм · Комедия',
    );
  });

  it('пропускает отсутствующие поля', () => {
    expect(movieMeta({ year: null, runtimeMinutes: null, genres: [] })).toBe('');
  });
});

const status = (state: SourceStatus['state'], enabled = true): SourceStatus => ({
  id: state,
  type: 'api',
  enabled,
  state,
  latencyMs: null,
  checkedAt: null,
});

describe('plural', () => {
  it.each([
    [1, 'серия'],
    [3, 'серии'],
    [5, 'серий'],
    [11, 'серий'],
    [21, 'серия'],
    [112, 'серий'],
  ])('%i → %s', (n, expected) => {
    expect(plural(n, 'серия', 'серии', 'серий')).toBe(expected);
  });
});

describe('closestEpisode', () => {
  it('оставляет серию, если она есть', () => {
    expect(closestEpisode([1, 2, 3], 2)).toBe(2);
  });

  it('берёт ближайшую предыдущую, если серии нет', () => {
    expect(closestEpisode([1, 2, 12], 15)).toBe(12);
    expect(closestEpisode([1, 2, 5, 6], 4)).toBe(2);
  });

  it('берёт первую, если предыдущих нет', () => {
    expect(closestEpisode([5, 6], 2)).toBe(5);
  });

  it('возвращает null для пустого списка', () => {
    expect(closestEpisode([], 1)).toBeNull();
  });
});

describe('sourcesSummary', () => {
  it('не считает выключенные источники', () => {
    const summary = sourcesSummary([status('online'), status('offline'), status('disabled', false)]);
    expect(summary.label).toBe('1 из 2 источников в сети');
    expect(summary.tone).toBe('warn');
  });

  it('не показывает «0 в сети», пока первая проверка не закончилась', () => {
    const summary = sourcesSummary([status('unknown'), status('online')]);
    expect(summary.label).toBe('Проверяем источники…');
    expect(summary.tone).toBe('');
  });

  it('сообщает, когда включённых нет', () => {
    expect(sourcesSummary([status('disabled', false)]).tone).toBe('bad');
  });
});

describe('titleMeta', () => {
  it('не показывает число серий у фильма', () => {
    expect(titleMeta({ kind: 'movie', year: 1997, episodesTotal: 1 })).toBe('Фильм · 1997');
  });

  it('собирает строку для сериала', () => {
    expect(titleMeta({ kind: 'tv', year: 2023, episodesTotal: 28 })).toBe('Сериал · 2023 · 28 серий');
  });
});
