import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { HistoryItem, Playback, SearchResult, SourceStatus, TitleCard, TitleDetails } from './models';

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
