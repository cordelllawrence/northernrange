using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Models;

namespace NorthernRange.Auth;

public class AuthService
{
    private const string UserId = "user";
    private static readonly string[] Scopes = [GmailService.Scope.GmailModify];

    private readonly ILogger<AuthService> _logger;

    public AuthService(ILogger<AuthService> logger)
    {
        _logger = logger;
    }

    public async Task<AuthLoginResult> LoginAsync(
        string credentialsPath,
        string tokenStorePath,
        bool force,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting OAuth2 login. CredentialsPath={CredentialsPath}", credentialsPath);

        EnsureSecretsFileExists(credentialsPath);

        var tokenStore = new FileDataStore(tokenStorePath, true);

        if (force)
        {
            await tokenStore.DeleteAsync<TokenResponse>(UserId);
            _logger.LogInformation("Existing token deleted (--force)");
        }

        UserCredential credential;
        await using var stream = File.OpenRead(credentialsPath);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;

        credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            secrets, Scopes, UserId, ct, tokenStore);

        // Fetch email via Gmail profile
        var service = new GmailService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "northernrange"
        });

        var profile = await service.Users.GetProfile("me").ExecuteAsync(ct);
        _logger.LogInformation("Login successful for {Email}", profile.EmailAddress);

        // Persist email locally for offline status check
        await PersistUserInfoAsync(tokenStorePath, profile.EmailAddress, ct);

        return new AuthLoginResult("authenticated", profile.EmailAddress);
    }

    public async Task LogoutAsync(string tokenStorePath, CancellationToken ct = default)
    {
        _logger.LogInformation("Logging out");

        var tokenStore = new FileDataStore(tokenStorePath, true);
        var token = await tokenStore.GetAsync<TokenResponse>(UserId);

        if (token?.RefreshToken != null)
        {
            try
            {
                using var http = new HttpClient();
                var content = new FormUrlEncodedContent(
                    [new KeyValuePair<string, string>("token", token.RefreshToken)]);
                var response = await http.PostAsync(
                    "https://oauth2.googleapis.com/revoke", content, ct);
                if (!response.IsSuccessStatusCode)
                    Console.Error.WriteLine(
                        $"Warning: Token revocation returned HTTP {(int)response.StatusCode}. Deleting local token anyway.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Warning: Could not revoke token remotely: {ex.Message}. Deleting local token anyway.");
                _logger.LogWarning(ex, "Token revocation network error");
            }
        }

        await tokenStore.DeleteAsync<TokenResponse>(UserId);

        var userInfoPath = Path.Combine(tokenStorePath, "user_info.json");
        if (File.Exists(userInfoPath)) File.Delete(userInfoPath);

        _logger.LogInformation("Token deleted from store");
    }

    public async Task<AuthStatusResult> GetStatusAsync(string tokenStorePath)
    {
        // No network call — reads from local store only
        var tokenStore = new FileDataStore(tokenStorePath, true);
        var token = await tokenStore.GetAsync<TokenResponse>(UserId);

        if (token == null || string.IsNullOrEmpty(token.RefreshToken))
            return new AuthStatusResult(false, null, null, false);

        var expiry = new DateTimeOffset(token.IssuedUtc, TimeSpan.Zero)
            .AddSeconds(token.ExpiresInSeconds ?? 3600);

        var valid = !string.IsNullOrEmpty(token.RefreshToken);
        var email = await LoadUserEmailAsync(tokenStorePath);

        return new AuthStatusResult(true, email, expiry, valid);
    }

    public async Task<UserCredential> GetCredentialAsync(
        string credentialsPath,
        string tokenStorePath,
        CancellationToken ct = default)
    {
        EnsureSecretsFileExists(credentialsPath);

        var tokenStore = new FileDataStore(tokenStorePath, true);
        var existingToken = await tokenStore.GetAsync<TokenResponse>(UserId);

        if (existingToken == null || string.IsNullOrEmpty(existingToken.RefreshToken))
            throw new NrException(ExitCodes.AuthRequired,
                "No credentials found. Run 'nr auth login' to authenticate.");

        await using var stream = File.OpenRead(credentialsPath);
        var secrets = GoogleClientSecrets.FromStream(stream).Secrets;

        var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = secrets,
            Scopes = Scopes,
            DataStore = tokenStore
        });

        var token = await flow.LoadTokenAsync(UserId, ct);
        if (token == null)
            throw new NrException(ExitCodes.AuthRequired,
                "No credentials found. Run 'nr auth login' to authenticate.");

        var credential = new UserCredential(flow, UserId, token);

        try
        {
            // Proactively refresh if the access token is expired
            if (credential.Token.IsStale)
                await credential.RefreshTokenAsync(ct);
        }
        catch (TokenResponseException ex)
        {
            _logger.LogError(ex, "Token refresh failed");
            throw new NrException(ExitCodes.AuthRequired,
                "Stored token is no longer valid. Run 'nr auth login' to re-authenticate.", ex);
        }

        return credential;
    }

    private static void EnsureSecretsFileExists(string path)
    {
        if (!File.Exists(path))
            throw new NrException(ExitCodes.GeneralError,
                $"Client secrets file not found at {path}. " +
                "Download it from the Google Cloud Console (OAuth 2.0 Client ID, Desktop type) " +
                $"and place it at {path}.");
    }

    private static async Task PersistUserInfoAsync(string tokenStorePath, string? email, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(email)) return;
        var path = Path.Combine(tokenStorePath, "user_info.json");
        var json = JsonSerializer.Serialize(
            new { email, loginTime = DateTimeOffset.UtcNow },
            new System.Text.Json.JsonSerializerOptions
            {
                TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
            });
        await File.WriteAllTextAsync(path, json, ct);
    }

    private static async Task<string?> LoadUserEmailAsync(string tokenStorePath)
    {
        var path = Path.Combine(tokenStorePath, "user_info.json");
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("email", out var el) ? el.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
