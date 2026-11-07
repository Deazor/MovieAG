using MovieAG.Infrastructure.Configuration;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

public class SourcesConfigParserTests
{
    private const string Valid = """
        # комментарий
        catalog:
          provider: shikimori
          url: https://shikimori.io

        sources:
          - id: anilibria
            type: api
            url: https://anilibria.top

          # токен из настроек
          - id: kodik
            type: balancer
            token: ${KODIK_TOKEN}
        """;

    private static SourcesConfig Parse(string yaml, string? token = null) =>
        SourcesConfigParser.Parse(yaml, name => name == "KODIK_TOKEN" ? token : null);

    [Fact]
    public void Parse_ReadsSourcesInOrderAndResolvesToken()
    {
        var config = Parse(Valid, "secret");

        Assert.Equal(["anilibria", "kodik"], config.Sources.Select(s => s.Id));
        Assert.Equal("secret", config.Sources[1].Token);
        Assert.True(config.Sources[0].Enabled);
    }

    [Fact]
    public void Parse_LeavesTokenEmptyWhenVariableIsMissing()
    {
        Assert.Null(Parse(Valid).Sources[1].Token);
    }

    [Theory]
    [InlineData("sources:\n  - id: a\n    type: magic", "type")]
    [InlineData("sources:\n  - type: api", "не указан id")]
    [InlineData("sources:\n  - id: a\n    type: api\n    url: ftp://x", "url")]
    [InlineData("sources:\n  - id: a\n    type: api\n  - id: a\n    type: api", "повторяется")]
    public void Parse_RejectsInvalidConfig(string yaml, string expected)
    {
        var ex = Assert.Throws<SourcesConfigException>(() => Parse(yaml));
        Assert.Contains(ex.Errors, e => e.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ReportsBrokenYaml()
    {
        Assert.Throws<SourcesConfigException>(() => Parse("sources: [oops"));
    }

    [Fact]
    public void SetEnabled_InsertsFlagAndKeepsComments()
    {
        var result = SourcesConfigParser.SetEnabled(Valid, "kodik", false);

        Assert.Contains("# токен из настроек", result);
        var config = Parse(result);
        Assert.True(config.Sources[0].Enabled);
        Assert.False(config.Sources[1].Enabled);
    }

    [Fact]
    public void SetEnabled_UpdatesExistingFlagOfTheRightSource()
    {
        var disabled = SourcesConfigParser.SetEnabled(Valid, "anilibria", false);
        var config = Parse(SourcesConfigParser.SetEnabled(disabled, "anilibria", true));

        Assert.True(config.Sources[0].Enabled);
        Assert.Single(
            SourcesConfigParser.SetEnabled(disabled, "anilibria", true).Split('\n'),
            l => l.Contains("enabled:", StringComparison.Ordinal));
    }

    [Fact]
    public void SetEnabled_ThrowsForUnknownSource()
    {
        Assert.Throws<SourcesConfigException>(() => SourcesConfigParser.SetEnabled(Valid, "nope", true));
    }

    [Theory]
    [InlineData("anilibria", null, true, SourceReadiness.Ready)]
    [InlineData("kodik", null, true, SourceReadiness.NeedsToken)]
    [InlineData("kodik", "secret", true, SourceReadiness.Ready)]
    [InlineData("kodik", "secret", false, SourceReadiness.Disabled)]
    [InlineData("animego", null, true, SourceReadiness.NoAdapter)]
    public void GetReadiness(string id, string? token, bool enabled, SourceReadiness expected)
    {
        var definition = new SourceDefinition { Id = id, Type = "api", Token = token, Enabled = enabled };
        Assert.Equal(expected, SourceRegistry.GetReadiness(definition));
    }
}
