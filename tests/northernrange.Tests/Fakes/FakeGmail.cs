using System.Net;
using System.Text;
using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;

namespace NorthernRange.Tests.Fakes;

/// <summary>
/// A <see cref="GmailService"/> whose HTTP layer is an in-memory handler.
/// Register canned JSON per URL path, then assert on the recorded requests.
/// This is the supported seam in Google.Apis: the initializer's
/// <c>HttpClientFactory</c> decides which handler sends the request.
/// </summary>
public sealed class FakeGmail : IHttpClientFactory
{
    private readonly FakeHandler _handler = new();

    public GmailService Service { get; }

    public FakeGmail()
    {
        Service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientFactory = this,
            ApplicationName = "northernrange-tests",
            // No retries in tests: a 503 should surface immediately.
            DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
        });
    }

    /// <summary>All requests the service sent, in order.</summary>
    public IReadOnlyList<RecordedRequest> Requests => _handler.Requests;

    /// <summary>Responds with <paramref name="json"/> to every request whose path ends with <paramref name="pathSuffix"/>.</summary>
    public FakeGmail OnPath(string pathSuffix, string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _handler.Routes.Add((req => req.RequestUri!.AbsolutePath.EndsWith(pathSuffix, StringComparison.Ordinal), _ => (status, json)));
        return this;
    }

    /// <summary>Responds using a callback so the body can depend on the request.</summary>
    public FakeGmail On(Func<HttpRequestMessage, bool> match, Func<HttpRequestMessage, string> json)
    {
        _handler.Routes.Add((match, req => (HttpStatusCode.OK, json(req))));
        return this;
    }

    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args) =>
        new(new ConfigurableMessageHandler(_handler));

    public sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Query)
    {
        public string Path => Uri.AbsolutePath;
        public string? this[string key] => Query.GetValueOrDefault(key);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<RecordedRequest> Requests = [];
        public readonly List<(Func<HttpRequestMessage, bool> Match, Func<HttpRequestMessage, (HttpStatusCode, string)> Respond)> Routes = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, ParseQuery(request.RequestUri!)));

            foreach (var (match, respond) in Routes)
            {
                if (!match(request)) continue;
                var (status, json) = respond(request);
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"code\":404,\"message\":\"no fake route for " + request.RequestUri + "\"}}", Encoding.UTF8, "application/json"),
            });
        }

        private static Dictionary<string, string> ParseQuery(Uri uri)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString(eq < 0 ? pair : pair[..eq]);
                var val = Uri.UnescapeDataString(eq < 0 ? "" : pair[(eq + 1)..]);
                // Repeated keys (labelIds) are joined for easy assertion.
                dict[key] = dict.TryGetValue(key, out var existing) ? existing + "," + val : val;
            }
            return dict;
        }
    }
}
