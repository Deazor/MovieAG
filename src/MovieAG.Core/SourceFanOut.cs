using Microsoft.Extensions.Logging;

namespace MovieAG.Core;

/// <summary>Опрашивает источники параллельно; сбой одного не ломает выдачу остальных.</summary>
internal static class SourceFanOut
{
    public static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(10);

    public static async Task<(IReadOnlyList<T> Results, IReadOnlyList<string> Failed)> QueryAllAsync<TSource, T>(
        IReadOnlyList<TSource> active,
        Func<TSource, CancellationToken, Task<T>> call,
        ILogger logger,
        CancellationToken ct)
        where TSource : IMonitoredSource
    {
        var tasks = active.Select(async source =>
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SourceTimeout);
            try
            {
                return (source.Id, Ok: true, Value: await call(source, timeout.Token));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Источник {Source} не ответил", source.Id);
                return (source.Id, Ok: false, Value: default(T)!);
            }
        });

        var done = await Task.WhenAll(tasks);
        return (
            done.Where(d => d.Ok).Select(d => d.Value).ToList(),
            done.Where(d => !d.Ok).Select(d => d.Id).ToList());
    }
}
