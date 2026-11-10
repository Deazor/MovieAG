import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { BrowseFilters, BrowseResult, Genre } from './browse';
import {
  HistoryItem,
  MovieCard,
  MovieDetails,
  MovieListResult,
  MovieProgressItem,
  Playback,
  SearchResult,
  SourceStatus,
  TitleCard,
  TitleDetails,
} from './models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  search(query: string): Observable<SearchResult> {
    return this.http.get<SearchResult>('/api/search', { params: { q: query } });
  }

  fresh(): Observable<TitleCard[]> {
    return this.http.get<TitleCard[]>('/api/fresh');
  }

  title(id: number): Observable<TitleDetails> {
    return this.http.get<TitleDetails>(`/api/titles/${id}`);
  }

  playback(id: number, sourceId: string, dubId: string, episode: number): Observable<Playback> {
    const params = new HttpParams().set('source', sourceId).set('dub', dubId).set('episode', episode);
    return this.http.get<Playback>(`/api/titles/${id}/playback`, { params });
  }

  history(): Observable<HistoryItem[]> {
    return this.http.get<HistoryItem[]>('/api/history');
  }

  progress(id: number): Observable<HistoryItem> {
    return this.http.get<HistoryItem>(`/api/history/${id}`);
  }

  saveProgress(id: number, body: { episode: number; dubKey: string; dubName: string; sourceId: string }): Observable<void> {
    return this.http.put<void>(`/api/history/${id}`, body);
  }

  /** keepalive позволяет запросу завершиться, даже если страницу уже закрывают. */
  savePosition(id: number, body: { episode: number; seconds: number; durationSeconds: number | null }): Observable<void> {
    return this.http.put<void>(`/api/history/${id}/position`, body, { keepalive: true });
  }

  /** Без запроса возвращает последние фильмы источников. */
  movies(query: string): Observable<MovieListResult> {
    return this.http.get<MovieListResult>('/api/movies', { params: query ? { q: query } : {} });
  }

  movie(imdbId: string): Observable<MovieDetails> {
    return this.http.get<MovieDetails>(`/api/movies/${imdbId}`);
  }

  moviePlayback(imdbId: string, sourceId: string, streamId: string): Observable<Playback> {
    const params = new HttpParams().set('source', sourceId).set('stream', streamId);
    return this.http.get<Playback>(`/api/movies/${imdbId}/playback`, { params });
  }

  movieHistory(): Observable<MovieProgressItem[]> {
    return this.http.get<MovieProgressItem[]>('/api/movies/history');
  }

  movieProgress(imdbId: string): Observable<MovieProgressItem> {
    return this.http.get<MovieProgressItem>(`/api/movies/${imdbId}/progress`);
  }

  saveMovieProgress(
    imdbId: string,
    body: { sourceId: string; streamId: string; seconds: number; durationSeconds: number | null },
  ): Observable<void> {
    return this.http.put<void>(`/api/movies/${imdbId}/progress`, body, { keepalive: true });
  }

  animeGenres(): Observable<Genre[]> {
    return this.http.get<Genre[]>('/api/genres');
  }

  /** Аниме из каталога Shikimori по жанру, годам и сортировке; наличие в источниках видно в карточке тайтла. */
  browseAnime(query: string, filters: BrowseFilters, page: number): Observable<BrowseResult<TitleCard>> {
    return this.http.get<BrowseResult<TitleCard>>('/api/browse', { params: browseParams(query, filters, page) });
  }

  movieGenres(): Observable<Genre[]> {
    return this.http.get<Genre[]>('/api/movies/genres');
  }

  /** Фильмы из каталога TMDB; без токена TMDB сервер отвечает 503. */
  browseMovies(query: string, filters: BrowseFilters, page: number): Observable<BrowseResult<MovieCard>> {
    return this.http.get<BrowseResult<MovieCard>>('/api/movies/browse', { params: browseParams(query, filters, page) });
  }

  sources(): Observable<SourceStatus[]> {
    return this.http.get<SourceStatus[]>('/api/sources');
  }

  checkSources(): Observable<SourceStatus[]> {
    return this.http.post<SourceStatus[]>('/api/sources/check', null);
  }

  setSourceEnabled(id: string, enabled: boolean): Observable<SourceStatus[]> {
    return this.http.put<SourceStatus[]>(`/api/sources/${encodeURIComponent(id)}/enabled`, { enabled });
  }

  config(): Observable<string> {
    return this.http.get<{ yaml: string }>('/api/sources/config').pipe(map((r) => r.yaml));
  }

  saveConfig(yaml: string): Observable<SourceStatus[]> {
    return this.http.put<SourceStatus[]>('/api/sources/config', { yaml });
  }
}

function browseParams(query: string, filters: BrowseFilters, page: number): HttpParams {
  let params = new HttpParams();
  if (query) params = params.set('q', query);
  if (filters.genre) params = params.set('genre', filters.genre);
  if (filters.from !== null) params = params.set('from', filters.from);
  if (filters.to !== null) params = params.set('to', filters.to);
  if (filters.sort) params = params.set('sort', filters.sort);
  if (page > 1) params = params.set('page', page);
  return params;
}
