using Google.Apis.Gmail.v1;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;

namespace NorthernRange.Gmail;

/// <summary>
/// Builds and caches an authenticated <see cref="GmailService"/> per token-store
/// path (i.e. per account), so repeated calls within one invocation reuse the
/// same client and credential. The cache is guarded by a semaphore for safety
/// even though the CLI is effectively single-threaded per command.
/// </summary>
public class GmailClientFactory
{
    private readonly AuthService _authService;
    private readonly ILogger<GmailClientFactory> _logger;

    private readonly Dictionary<string, GmailService> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public GmailClientFactory(AuthService authService, ILogger<GmailClientFactory> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    public async Task<GmailService> GetServiceAsync(
        string credentialsPath,
        string tokenStorePath,
        int httpTimeoutSeconds = 30,
        CancellationToken ct = default)
    {
        var cacheKey = Path.GetFullPath(tokenStorePath);

        if (_cache.TryGetValue(cacheKey, out var existing))
            return existing;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(cacheKey, out existing))
                return existing;

            _logger.LogDebug("Creating GmailService for {TokenStore}", tokenStorePath);
            var credential = await _authService.GetCredentialAsync(credentialsPath, tokenStorePath, ct);

            var service = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "northernrange",
                // Retry transient 503 responses and network exceptions with
                // exponential backoff (1s, 2s, 4s, …) before surfacing an error.
                DefaultExponentialBackOffPolicy =
                    ExponentialBackOffPolicy.UnsuccessfulResponse503 | ExponentialBackOffPolicy.Exception
            });

            if (httpTimeoutSeconds > 0)
                service.HttpClient.Timeout = TimeSpan.FromSeconds(httpTimeoutSeconds);

            _cache[cacheKey] = service;
            return service;
        }
        finally
        {
            _lock.Release();
        }
    }
}
