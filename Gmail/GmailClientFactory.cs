using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;

namespace NorthernRange.Gmail;

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
                ApplicationName = "northernrange"
            });

            _cache[cacheKey] = service;
            return service;
        }
        finally
        {
            _lock.Release();
        }
    }
}
