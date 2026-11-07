using System.Net;
using System.Text;

namespace MovieAG.Tests;

/// <summary>Отвечает заранее заданным JSON по подстроке адреса и запоминает запросы.</summary>
internal sealed class StubHandler(params (string UrlPart, string Json)[] routes) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public HttpStatusCode? ForcedStatus { get; init; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);

        if (ForcedStatus is { } status)
        {
            return Task.FromResult(new HttpResponseMessage(status));
        }

        var url = request.RequestUri!.ToString();
        var route = routes.FirstOrDefault(r => url.Contains(r.UrlPart, StringComparison.Ordinal));
        return Task.FromResult(route.Json is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(route.Json, Encoding.UTF8, "application/json")
            });
    }
}
