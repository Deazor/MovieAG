import { describe, expect, it } from 'vitest';
import { EMPTY_FILTERS, filtersToParams, hasFilters, parseFilters, parsePage, parseYear } from './browse';

describe('parseFilters', () => {
  it('принимает корректные параметры адреса', () => {
    expect(parseFilters({ genre: '22', from: '2015', to: '2020', sort: 'rating' })).toEqual({
      genre: '22',
      from: 2015,
      to: 2020,
      sort: 'rating',
    });
  });

  it('отбрасывает подделанные значения', () => {
    expect(parseFilters({ genre: '22;drop', from: 'abc', to: '1800', sort: 'random' })).toEqual(EMPTY_FILTERS);
  });
});

describe('parseYear', () => {
  it('принимает число и строку', () => {
    expect(parseYear(2019)).toBe(2019);
    expect(parseYear('2019')).toBe(2019);
  });

  it('не принимает пустое значение и далёкое будущее', () => {
    expect(parseYear('')).toBeNull();
    expect(parseYear(3000)).toBeNull();
  });
});

describe('parsePage', () => {
  it('по умолчанию первая страница', () => {
    expect(parsePage(undefined)).toBe(1);
    expect(parsePage('0')).toBe(1);
    expect(parsePage('9999')).toBe(1);
    expect(parsePage('3')).toBe(3);
  });
});

describe('hasFilters / filtersToParams', () => {
  it('пустые фильтры не считаются выбранными', () => {
    expect(hasFilters(EMPTY_FILTERS)).toBe(false);
    expect(hasFilters({ ...EMPTY_FILTERS, sort: 'date' })).toBe(true);
  });

  it('сбрасывает страницу при смене фильтров', () => {
    expect(filtersToParams({ genre: '22' })).toEqual({ genre: '22', page: null });
    expect(filtersToParams(EMPTY_FILTERS)).toEqual({ genre: null, from: null, to: null, sort: null, page: null });
  });
});
