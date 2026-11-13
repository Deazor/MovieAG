export type DubKind = 'voice' | 'subtitles';
export type PlaybackKind = 'iframe' | 'hls';
export type SourceState = 'unknown' | 'online' | 'offline' | 'disabled' | 'needsToken' | 'noAdapter';

export interface TitleCard {
  id: number;
  name: string;
  originalName: string | null;
  kind: string | null;
  year: number | null;
  episodesTotal: number | null;
  posterUrl: string | null;
  dubs: string[];
  sources: string[];
  latestEpisode: number | null;
  hasMoreDubs: boolean;
  /** Оценка Shikimori; null, пока оценок нет. */
  score: number | null;
}

/** Оценка по десятибалльной шкале: IMDb, КП, TMDB или Shikimori. */
export interface Rating {
  source: string;
  value: number;
}

export interface SearchResult {
  items: TitleCard[];
  queriedSources: string[];
  failedSources: string[];
}

export interface CatalogTitle {
  id: number;
  name: string;
  originalName: string | null;
  japaneseName: string | null;
  kind: string | null;
  year: number | null;
  episodesTotal: number | null;
  status: string | null;
  description: string | null;
  posterUrl: string | null;
  studio: string | null;
  genres: string[];
  score: number | null;
}

export interface DubSource {
  sourceId: string;
  dubId: string;
  label: string;
  episodes: number[];
}

export interface DubGroup {
  key: string;
  name: string;
  kind: DubKind;
  episodes: number[];
  sources: DubSource[];
}

export interface TitleDetails {
  title: CatalogTitle;
  dubs: DubGroup[];
  failedSources: string[];
}

export interface PlaybackQuality {
  label: string;
  url: string;
}

export interface Playback {
  kind: PlaybackKind;
  url: string;
  qualities: PlaybackQuality[];
}

export interface HistoryItem {
  titleId: number;
  name: string;
  posterUrl: string | null;
  lastEpisode: number;
  episodesTotal: number | null;
  watchedCount: number;
  dubKey: string;
  dubName: string;
  sourceId: string;
  watchedEpisodes: number[];
  positionSeconds: number;
  durationSeconds: number | null;
}

export interface MovieCard {
  imdbId: string;
  name: string;
  originalName: string | null;
  year: number | null;
  posterUrl: string | null;
  sources: string[];
  ratings: Rating[];
}

export interface MovieListResult {
  items: MovieCard[];
  queriedSources: string[];
  failedSources: string[];
}

export interface MovieTitle {
  imdbId: string;
  name: string;
  originalName: string | null;
  year: number | null;
  runtimeMinutes: number | null;
  description: string | null;
  posterUrl: string | null;
  genres: string[];
  ratings: Rating[];
}

export interface MovieStream {
  sourceId: string;
  streamId: string;
  label: string;
  license: string | null;
}

export interface MovieDetails {
  movie: MovieTitle;
  streams: MovieStream[];
  failedSources: string[];
}

export interface MovieProgressItem {
  imdbId: string;
  name: string;
  posterUrl: string | null;
  sourceId: string;
  streamId: string;
  positionSeconds: number;
  durationSeconds: number | null;
}

export interface SourceStatus {
  id: string;
  type: string;
  enabled: boolean;
  state: SourceState;
  latencyMs: number | null;
  checkedAt: string | null;
}

export interface SeriesTitle {
  imdbId: string;
  name: string;
  originalName: string | null;
  year: number | null;
  seasonsCount: number | null;
  description: string | null;
  posterUrl: string | null;
  genres: string[];
  ratings: Rating[];
}

export interface SeriesEpisode {
  season: number;
  episode: number;
}

/** Одна озвучка сериала у одного источника и серии, где она есть. */
export interface SeriesStream {
  sourceId: string;
  streamId: string;
  label: string;
  episodes: SeriesEpisode[];
}

export interface SeriesDetails {
  series: SeriesTitle;
  streams: SeriesStream[];
  failedSources: string[];
}

export interface SeriesProgressItem {
  imdbId: string;
  name: string;
  posterUrl: string | null;
  sourceId: string;
  streamId: string;
  streamLabel: string;
  season: number;
  episode: number;
  positionSeconds: number;
  durationSeconds: number | null;
}
