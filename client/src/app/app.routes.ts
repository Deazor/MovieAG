import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/home/home').then((m) => m.Home), title: 'MovieAG' },
  { path: 'search', loadComponent: () => import('./pages/search/search').then((m) => m.Search), title: 'Каталог · MovieAG' },
  { path: 'title/:id', loadComponent: () => import('./pages/title/title').then((m) => m.TitlePage), title: 'Тайтл · MovieAG' },
  { path: 'watch/:id', loadComponent: () => import('./pages/player/player').then((m) => m.Player), title: 'Просмотр · MovieAG' },
  { path: 'movies', loadComponent: () => import('./pages/movies/movies').then((m) => m.Movies), title: 'Фильмы · MovieAG' },
  { path: 'movies/:imdbId', loadComponent: () => import('./pages/movie/movie').then((m) => m.Movie), title: 'Фильм · MovieAG' },
  { path: 'series', loadComponent: () => import('./pages/series-list/series-list').then((m) => m.SeriesList), title: 'Сериалы · MovieAG' },
  { path: 'series/:imdbId', loadComponent: () => import('./pages/series/series').then((m) => m.Series), title: 'Сериал · MovieAG' },
  { path: 'library', loadComponent: () => import('./pages/library/library').then((m) => m.Library), title: 'Библиотека · MovieAG' },
  { path: 'sources', loadComponent: () => import('./pages/sources/sources').then((m) => m.Sources), title: 'Источники · MovieAG' },
  { path: '**', redirectTo: '' },
];
