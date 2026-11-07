using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MovieAG.Core;
using MovieAG.Infrastructure.Configuration;
using Quartz;

namespace MovieAG.Infrastructure.Sources;

public enum SourceState { Unknown, Online, Offline, Disabled, NeedsToken, NoAdapter }

public sealed record SourceStatus(string Id, string Type, bool Enabled, SourceState State, int? LatencyMs, DateTimeOffset? CheckedAt);

public sealed class SourceHealthService(
    SourcesConfigStore store,
    ISourceProvider sources,
    ILogger<SourceHealthService> logger)
{
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, (bool Online, int LatencyMs, DateTimeOffset CheckedAt)> _results = new();

    public IReadOnlyList<SourceStatus> GetStatuses() => store.Current.Sources.Select(ToStatus).ToList();

    public async Task CheckAllAsync(CancellationToken ct)
    {
        await Task.WhenAll(sources.Active.Select(source => CheckAsync(source, ct)));
    }

    private async Task CheckAsync(IAnimeSource source, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(PingTimeout);
        var watch = Stopwatch.StartNew();
        var online = true;
        try
        {
            await source.PingAsync(timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            online = false;
            logger.LogWarning(ex, "Проверка источника {Source} не прошла", source.Id);
        }

        _results[source.Id] = (online, (int)watch.ElapsedMilliseconds, DateTimeOffset.UtcNow);
    }

    private SourceStatus ToStatus(SourceDefinition definition)
    {
        var state = SourceRegistry.GetReadiness(definition) switch
        {
            SourceReadiness.Disabled => SourceState.Disabled,
            SourceReadiness.NeedsToken => SourceState.NeedsToken,
            SourceReadiness.NoAdapter => SourceState.NoAdapter,
            _ => SourceState.Unknown
        };

        if (state != SourceState.Unknown || !_results.TryGetValue(definition.Id, out var result))
        {
            return new SourceStatus(definition.Id, definition.Type, definition.Enabled, state, null, null);
        }

        return new SourceStatus(
            definition.Id,
            definition.Type,
            definition.Enabled,
            result.Online ? SourceState.Online : SourceState.Offline,
            result.Online ? result.LatencyMs : null,
            result.CheckedAt);
    }
}

[DisallowConcurrentExecution]
public sealed class SourceHealthJob(SourceHealthService health) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken ct) => await health.CheckAllAsync(ct);
}
