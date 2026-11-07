# MovieAG

Локальный агрегатор аниме: ищет тайтл сразу во всех источниках из `sources.yaml`, склеивает результаты по ID Shikimori и показывает все озвучки в одном плеере.

Стек: .NET 10, ASP.NET Core, EF Core, PostgreSQL, Quartz.NET, Angular, hls.js, xUnit.

## Запуск

Нужны .NET SDK 10, Node 24 и Docker.

```bash
docker compose up -d --wait
```

```bash
dotnet run --project src/MovieAG.Api --launch-profile http
```

```bash
npm --prefix client install
```

```bash
npm --prefix client start
```

Приложение откроется на http://localhost:4200, Swagger на http://localhost:5140/swagger. Миграции применяются при старте API.

## Источники

Список хранится в `sources.yaml`, его можно менять в файле или на экране «Источники». Порядок задаёт приоритет.

| Поле | Значение |
|---|---|
| `id` | Уникальное имя; по нему же выбирается адаптер |
| `type` | `balancer`, `api` или `site` |
| `url` | Базовый адрес API |
| `token` | Токен, обычно ссылка `${NAME}` на настройку |
| `adapter` | Имя адаптера, если отличается от `id` |
| `enabled` | `false` выключает источник |

Адаптеры есть для `anilibria` (без токена, HLS), `yummy` (без токена, iframe разных плееров) и `kodik` (нужен токен, iframe). Для остальных `id` экран покажет «Нет адаптера».

У `yummy` поиск не называет озвучки: их полный список появляется в карточке тайтла. Если получите client_id на yummyani.me/dev/applications, укажите его в поле `token`.

Токен Kodik лучше хранить в user-secrets, а не в файле:

```bash
dotnet user-secrets set KODIK_TOKEN "ваш-токен" --project src/MovieAG.Api
```

## История просмотра

Для каждого тайтла сохраняются серия, озвучка, источник и позиция в серии. Встроенные плееры сообщают время через `postMessage`, форматы описаны в `client/src/app/core/player-bridge.ts`.

| Плеер | Сохранение времени | Автоперемотка |
|---|---|---|
| HLS (AniLibria) | Да | Да |
| Kodik | Да | Да |
| CVH | Да | Да, после рекламы |
| Aksor | Да | Нет, приложение подсказывает время для ручной перемотки |
| Alloha | Нет | Нет |

## Новый источник

1. Реализуйте `IAnimeSource` в `src/MovieAG.Infrastructure/Sources`.
2. Зарегистрируйте адаптер в словаре `Adapters` в `SourceRegistry`.
3. Добавьте запись в `sources.yaml`.

В выдачу попадают только тайтлы с ID Shikimori: по нему работает склейка.

## Тесты

```bash
dotnet test
```

```bash
npm --prefix client test -- --watch=false
```

## Безопасность

У API нет авторизации: приложение рассчитано на одного пользователя и слушает только `localhost`. Перед публикацией в сеть нужно добавить аутентификацию. Пароль в `compose.yaml` предназначен только для локальной разработки.
