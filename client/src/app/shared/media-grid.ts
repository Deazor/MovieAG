import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { imdbSeed } from '../core/format';
import { MovieCard } from '../core/models';
import { Poster } from './poster';
import { Ratings } from './ratings';

/** Сетка карточек по IMDb ID: фильмы ведут на /movies/tt…, сериалы — на /series/tt…. */
@Component({
  selector: 'app-media-grid',
  imports: [RouterLink, Poster, Ratings],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="grid">
      @for (item of items(); track item.imdbId) {
        <a class="card" [routerLink]="[basePath(), item.imdbId]">
          <app-poster [url]="item.posterUrl" [text]="item.originalName ?? item.name" [seed]="seed(item.imdbId)" />
          <div class="card-name">{{ item.name }}</div>
          <app-ratings [ratings]="item.ratings" />
          @if (item.year || showImdbId()) {
            <div class="muted small">
              {{ item.year }}@if (item.year && showImdbId()) { · }@if (showImdbId()) {<span class="mono">{{ item.imdbId }}</span>}
            </div>
          }
        </a>
      }
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(min(168px, 100%), 1fr));
      gap: 24px 16px;
    }

    .card {
      display: flex;
      flex-direction: column;
      gap: 8px;
      min-width: 0;
      text-decoration: none;
    }

    .card-name { font-size: 15px; font-weight: 600; text-wrap: pretty; }
    .small { font-size: 14px; }
  `,
})
export class MediaGrid {
  readonly items = input.required<MovieCard[]>();
  readonly basePath = input.required<'/movies' | '/series'>();
  readonly showImdbId = input(false);

  protected readonly seed = imdbSeed;
}
