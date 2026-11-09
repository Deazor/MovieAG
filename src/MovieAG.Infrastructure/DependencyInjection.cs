using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MovieAG.Core;
using MovieAG.Infrastructure.Catalog;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Persistence;
using MovieAG.Infrastructure.Sources;
using Quartz;

namespace MovieAG.Infrastructure;

public enum DatabaseProvider { Postgres, Sqlite }

public sealed record InfrastructureOptions(DatabaseProvider Database, string ConnectionString, string SourcesConfigPath);

public static class DependencyInjection
{
    private const string UserAgent = "MovieAG/0.1 (personal project)";
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan HealthCheckInterval = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddMovieAgInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        InfrastructureOptions options)
    {
        var sourcesConfigPath = options.SourcesConfigPath;

        // Код работает с AppDbContext; для SQLite подставляется наследник со своими миграциями.
        switch (options.Database)
        {
            case DatabaseProvider.Postgres:
                services.AddDbContext<AppDbContext>(db => db.UseNpgsql(options.ConnectionString));
                break;
            case DatabaseProvider.Sqlite:
                services.AddDbContext<AppDbContext, SqliteDbContext>(db => db.UseSqlite(options.ConnectionString));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options), options.Database, "Неизвестный провайдер базы");
        }

        services.AddMemoryCache();

        services.AddHttpClient(SourceRegistry.HttpClientName, ConfigureClient);
        services.AddHttpClient<ShikimoriCatalog>(ConfigureClient);
        services.AddHttpClient<TmdbCatalog>(ConfigureClient);

        services.AddSingleton(_ => new SourcesConfigStore(sourcesConfigPath, configuration));
        services.AddSingleton<SourceRegistry>();
        services.AddSingleton<ISourceProvider>(sp => sp.GetRequiredService<SourceRegistry>());
        services.AddSingleton<IMovieSourceProvider>(sp => sp.GetRequiredService<SourceRegistry>());
        services.AddSingleton<SourceHealthService>();
        services.AddScoped<ICatalog, CachedCatalog>();
        services.AddScoped<AggregationService>();
        services.AddTransient<IMovieCatalog>(sp => sp.GetRequiredService<TmdbCatalog>());
        services.AddScoped<MovieService>();

        services.AddQuartz(quartz =>
        {
            var jobKey = new JobKey(nameof(SourceHealthJob));
            quartz.AddJob<SourceHealthJob>(job => job.WithIdentity(jobKey));
            quartz.AddTrigger(trigger => trigger
                .ForJob(jobKey)
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(HealthCheckInterval).RepeatForever()));
        });
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = false);

        return services;
    }

    private static void ConfigureClient(HttpClient client)
    {
        client.Timeout = HttpTimeout;
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }
}
