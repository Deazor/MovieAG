using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MovieAG.Hosting;
using MovieAG.Infrastructure;

namespace MovieAG.Desktop;

/// <summary>Встроенный сервер: тот же API и клиент, что в веб-версии, но на SQLite и случайном локальном порту.</summary>
internal sealed class DesktopServer : IAsyncDisposable
{
    private const string ExampleConfig = "sources.example.yaml";
    private const string SourcesRoute = "/sources";

    private readonly WebApplication _app;

    private DesktopServer(WebApplication app, DesktopPaths paths, Uri startUri)
    {
        _app = app;
        Paths = paths;
        StartUri = startUri;
    }

    public DesktopPaths Paths { get; }

    public Uri StartUri { get; }

    public static async Task<DesktopServer> StartAsync(DesktopPaths paths)
    {
        var isFirstRun = EnsureSourcesConfig(paths);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });

        // Порт 0: система выдаст свободный. Слушаем только локальный адрес, а Host проверяем от подмены через DNS.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["AllowedHosts"] = "127.0.0.1;localhost";

        var options = new MovieAgHostOptions
        {
            Database = DatabaseProvider.Sqlite,
            ConnectionString = $"Data Source={paths.Database}",
            SourcesConfigPath = paths.SourcesConfig,
            ClientRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        };
        builder.AddMovieAg(options);

        var app = builder.Build();
        await app.UseMovieAgAsync(options);
        await app.StartAsync();

        var address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        var startUri = new Uri(new Uri(address), isFirstRun ? SourcesRoute : "/");
        return new DesktopServer(app, paths, startUri);
    }

    public async Task StopAsync()
    {
        await _app.StopAsync();
        await DisposeAsync();
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();

    /// <summary>При первом запуске кладёт пример конфига, чтобы приложение открылось, а не упало.</summary>
    private static bool EnsureSourcesConfig(DesktopPaths paths)
    {
        if (File.Exists(paths.SourcesConfig)) return false;

        File.Copy(Path.Combine(AppContext.BaseDirectory, ExampleConfig), paths.SourcesConfig);
        return true;
    }
}
