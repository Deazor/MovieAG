using Microsoft.Extensions.Configuration;
using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

public class SourcesConfigStoreTests
{
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().Build();

    [Fact]
    public void Constructor_ExplainsWhatToDoWhenConfigIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".yaml");

        var ex = Assert.Throws<FileNotFoundException>(() => new SourcesConfigStore(path, EmptyConfiguration));

        Assert.Contains("sources.example.yaml", ex.Message);
    }

    [Fact]
    public void ExampleConfig_IsValidAndContainsOnlyPlaceholders()
    {
        var store = new SourcesConfigStore(FindExampleConfig(), EmptyConfiguration);

        Assert.NotEmpty(store.Current.Sources);
        Assert.All(store.Current.Sources, source =>
        {
            Assert.Contains("example.com", source.Url);
            Assert.NotEqual(SourceReadiness.Ready, SourceRegistry.GetReadiness(source));
        });
    }

    private static string FindExampleConfig()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "sources.example.yaml");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("sources.example.yaml не найден выше каталога тестов");
    }
}
