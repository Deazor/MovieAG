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
}

export interface DubSource {
  sourceId: string;
  dubId: string;
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
}

export interface SourceStatus {
  id: string;
  type: string;
  enabled: boolean;
  state: SourceState;
  latencyMs: number | null;
  checkedAt: string | null;
}
