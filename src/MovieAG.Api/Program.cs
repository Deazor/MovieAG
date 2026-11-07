using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using MovieAG.Api;
using MovieAG.Infrastructure;
using MovieAG.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var sourcesPath = Path.GetFullPath(
    builder.Configuration["SourcesConfigPath"] ?? "sources.yaml",
    builder.Environment.ContentRootPath);

builder.Services.AddMovieAgInfrastructure(builder.Configuration, sourcesPath);
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase)));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMovieAgApi();

app.Run();
