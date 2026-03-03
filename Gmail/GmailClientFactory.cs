using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;

namespace NorthernRange.Gmail;

public class GmailClientFactory
{
    private readonly AuthService _authService;
    private readonly ILogger<GmailClientFactory> _logger;

    private GmailService? _cached;
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
        if (_cached is not null) return _cached;

        await _lock.WaitAsync(ct);
        try
        {
            if (_cached is not null) return _cached;

            _logger.LogDebug("Creating GmailService instance");
            var credential = await _authService.GetCredentialAsync(credentialsPath, tokenStorePath, ct);

            _cached = new GmailService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "northernrange"
            });

            return _cached;
        }
        finally
        {
            _lock.Release();
        }
    }
}
