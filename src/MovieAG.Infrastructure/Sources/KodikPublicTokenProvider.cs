using System.Text.RegularExpressions;

namespace MovieAG.Infrastructure.Sources;

/// <summary>Выдаёт токен Kodik API, когда в конфиге собственный токен не указан.</summary>
public interface IKodikTokenProvider
{
    ValueTask<string> GetTokenAsync(CancellationToken ct);

    /// <summary>Сбрасывает кэш, если там всё ещё лежит отвергнутый токен.</summary>
    void Invalidate(string rejectedToken);
}

/// <summary>
/// Достаёт публичный токен из скрипта встраивания плееров Kodik, как getPublicToken в kodikwrapper.
/// Токен кэшируется: скрипт скачивается один раз за <see cref="CacheDuration"/> или после отказа API.
/// </summary>
public sealed partial class KodikPublicTokenProvider(IHttpClientFactory httpClients, TimeProvider time) : IKodikTokenProvider, IDisposable
{
    public const string PlayersScriptUrl = "https://kodik-add.com/add-players.min.js?v=2";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(6);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private volatile CachedToken? _cached;

    public async ValueTask<string> GetTokenAsync(CancellationToken ct)
    {
        if (TryGetFresh() is { } token) return token;

        await _lock.WaitAsync(ct);
        try
        {
            // Пока ждали блокировку, токен мог скачать параллельный запрос.
            if (TryGetFresh() is { } fresh) return fresh;

            var fetched = await FetchAsync(ct);
            _cached = new CachedToken(fetched, time.GetUtcNow() + CacheDuration);
            return fetched;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Invalidate(string rejectedToken)
    {
        var cached = _cached;
        if (cached is not null && cached.Value == rejectedToken)
        {
            Interlocked.CompareExchange(ref _cached, null, cached);
        }
    }

    public void Dispose() => _lock.Dispose();

    /// <summary>Ищет в тексте скрипта присваивание вида token="hex" или token:'hex'.</summary>
    public static string? ExtractToken(string script) =>
        TokenRegex().Match(script) is { Success: true } match ? match.Groups["token"].Value : null;

    private string? TryGetFresh()
    {
        var cached = _cached;
        return cached is not null && cached.ExpiresAt > time.GetUtcNow() ? cached.Value : null;
    }

    private async Task<string> FetchAsync(CancellationToken ct)
    {
        var http = httpClients.CreateClient(SourceRegistry.HttpClientName);
        using var response = await http.GetAsync(PlayersScriptUrl, ct);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Скрипт плееров Kodik вернул {(int)response.StatusCode}", null, response.StatusCode);
        }

        var script = await response.Content.ReadAsStringAsync(ct);
        return ExtractToken(script)
            ?? throw new InvalidOperationException("Не удалось найти публичный токен в скрипте плееров Kodik");
    }

    [GeneratedRegex("""token\s*[=:]\s*["'](?<token>[0-9a-f]+)["']""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    private sealed record CachedToken(string Value, DateTimeOffset ExpiresAt);
}
