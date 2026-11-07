using Microsoft.Extensions.Configuration;

namespace MovieAG.Infrastructure.Configuration;

/// <summary>Держит sources.yaml и разобранный конфиг; запись атомарна и идёт только после проверки.</summary>
public sealed class SourcesConfigStore
{
    private readonly string _path;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private volatile SourcesConfig _current;

    public SourcesConfigStore(string path, IConfiguration configuration)
    {
        _path = path;
        _configuration = configuration;
        _current = SourcesConfigParser.Parse(File.ReadAllText(path), Resolve);
    }

    public SourcesConfig Current => _current;

    public event Action? Changed;

    public Task<string> ReadRawAsync(CancellationToken ct) => File.ReadAllTextAsync(_path, ct);

    public async Task SaveRawAsync(string yaml, CancellationToken ct)
    {
        var parsed = SourcesConfigParser.Parse(yaml, Resolve);

        await _writeLock.WaitAsync(ct);
        try
        {
            await WriteAsync(yaml, parsed, ct);
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke();
    }

    public async Task SetEnabledAsync(string id, bool enabled, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            var yaml = SourcesConfigParser.SetEnabled(await File.ReadAllTextAsync(_path, ct), id, enabled);
            await WriteAsync(yaml, SourcesConfigParser.Parse(yaml, Resolve), ct);
        }
        finally
        {
            _writeLock.Release();
        }

        Changed?.Invoke();
    }

    private async Task WriteAsync(string yaml, SourcesConfig parsed, CancellationToken ct)
    {
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, yaml, ct);
        File.Move(temp, _path, overwrite: true);
        _current = parsed;
    }

    private string? Resolve(string name) => _configuration[name];
}
