using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using MovieAG.Infrastructure;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Persistence;

namespace MovieAG.Hosting;

public sealed class MovieAgHostOptions
{
    public required DatabaseProvider Database { get; init; }
    public required string ConnectionString { get; init; }
    public required string SourcesConfigPath { get; init; }

    /// <summary>Папка с собранным Angular. Если в ней нет index.html, клиент не раздаётся.</summary>
    public string? ClientRoot { get; init; }
}

/// <summary>Общая сборка сервера для веб-версии и desktop-приложения.</summary>
public static class MovieAgHost
{
    private const string ClientEntry = "index.html";

    public static WebApplicationBuilder AddMovieAg(this WebApplicationBuilder builder, MovieAgHostOptions options)
    {
        builder.Services.AddMovieAgInfrastructure(
            builder.Configuration,
            new InfrastructureOptions(options.Database, options.ConnectionString, options.SourcesConfigPath));
        builder.Services.AddProblemDetails();
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        return builder;
    }

    public static async Task UseMovieAgAsync(this WebApplication app, MovieAgHostOptions options)
    {
        // Без конфига приложение бесполезно: сообщаем об этом сразу, а не при первом запросе.
        app.Services.GetRequiredService<SourcesConfigStore>();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        var clientFiles = CreateClientFileProvider(options.ClientRoot);
        if (clientFiles is not null)
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = clientFiles });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = clientFiles });
        }

        app.MapMovieAgApi();

        if (clientFiles is not null)
        {
            // Маршруты Angular отдают index.html, а неизвестный адрес API — честный 404.
            app.MapFallback("/api/{**path}", () => Results.NotFound());
            app.MapFallbackToFile(ClientEntry, new StaticFileOptions { FileProvider = clientFiles });
        }
    }

    private static PhysicalFileProvider? CreateClientFileProvider(string? root) =>
        root is not null && File.Exists(Path.Combine(root, ClientEntry))
            ? new PhysicalFileProvider(Path.GetFullPath(root))
            : null;
}
