using System.Net;
using System.Text;
using MovieAG.Infrastructure.Sources;

namespace MovieAG.Tests;

public class KodikPublicTokenProviderTests
{
    private const string Script = """a=1;e.type="SCRIPT",e.token="447d179e875efe44217f20d1ee2146be";b=2""";

    [Theory]
    [InlineData(Script, "447d179e875efe44217f20d1ee2146be")]
    [InlineData("token:'deadbeef'", "deadbeef")]
    [InlineData("TOKEN = \"ABC123\"", "ABC123")]
    [InlineData("no token here", null)]
    [InlineData("token=\"not-hex\"", null)]
    public void ExtractToken_ParsesAssignment(string script, string? expected)
    {
        Assert.Equal(expected, KodikPublicTokenProvider.ExtractToken(script));
    }

    [Fact]
    public async Task GetToken_DownloadsPlayersScriptOnceAndCaches()
    {
        var handler = new ScriptHandler(Script);
        var provider = Create(handler, new ManualTime());

        var first = await provider.GetTokenAsync(CancellationToken.None);
        var second = await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal("447d179e875efe44217f20d1ee2146be", first);
        Assert.Equal(first, second);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(KodikPublicTokenProvider.PlayersScriptUrl, handler.LastUrl);
    }

    [Fact]
    public async Task GetToken_RefetchesAfterCacheExpires()
    {
        var handler = new ScriptHandler(Script);
        var time = new ManualTime();
        var provider = Create(handler, time);

        await provider.GetTokenAsync(CancellationToken.None);
        time.Advance(KodikPublicTokenProvider.CacheDuration + TimeSpan.FromSeconds(1));
        await provider.GetTokenAsync(CancellationToken.None);

        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Invalidate_DropsOnlyTheRejectedToken()
    {
        var handler = new ScriptHandler(Script);
        var provider = Create(handler, new ManualTime());
        var token = await provider.GetTokenAsync(CancellationToken.None);

        provider.Invalidate("other");
        await provider.GetTokenAsync(CancellationToken.None);
        Assert.Equal(1, handler.Calls);

        provider.Invalidate(token);
        await provider.GetTokenAsync(CancellationToken.None);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task GetToken_ThrowsWhenScriptHasNoToken()
    {
        var provider = Create(new ScriptHandler("no token here"), new ManualTime());

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetTokenAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task GetToken_ThrowsOnHttpError()
    {
        var provider = Create(new ScriptHandler(Script, HttpStatusCode.BadGateway), new ManualTime());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetTokenAsync(CancellationToken.None).AsTask());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
    }

    private static KodikPublicTokenProvider Create(HttpMessageHandler handler, TimeProvider time) =>
        new(new SingleClientFactory(handler), time);

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class ScriptHandler(string script, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public string? LastUrl { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastUrl = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(script, Encoding.UTF8, "application/javascript")
            });
        }
    }
}

internal sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}
