using MovieAG.Hosting;
using MovieAG.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var options = new MovieAgHostOptions
{
    Database = DatabaseProvider.Postgres,
    ConnectionString = builder.Configuration.GetConnectionString("Default")
        ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default"),
    SourcesConfigPath = Path.GetFullPath(
        builder.Configuration["SourcesConfigPath"] ?? "sources.yaml",
        builder.Environment.ContentRootPath),
    ClientRoot = builder.Environment.WebRootPath
};

builder.AddMovieAg(options);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

await app.UseMovieAgAsync(options);

app.Run();
