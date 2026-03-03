using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class AuthCommands
{
    private readonly AuthService _authService;
    private readonly ConfigLoader _configLoader;
    private readonly OutputWriter _output;
    private readonly ILogger<AuthCommands> _logger;

    public AuthCommands(
        AuthService authService,
        ConfigLoader configLoader,
        OutputWriter output,
        ILogger<AuthCommands> logger)
    {
        _authService = authService;
        _configLoader = configLoader;
        _output = output;
        _logger = logger;
    }

    [Command("login", Description = "Authenticate with Gmail via the OAuth2 browser flow. " +
        "Opens your default browser to Google's consent screen. " +
        "After you approve, a refresh token is stored locally and all subsequent commands authenticate silently — " +
        "you only need to run this once. " +
        "Requires a client_secrets.json file (see --credentials). " +
        "Example: nr auth login --credentials ~/secrets/client_secrets.json")]
    public async Task LoginAsync(
        GlobalOptions globals,
        [Option("force", Description = "Delete any existing stored token and re-run the full browser consent flow. " +
            "Use this if your token was revoked, if you want to switch Google accounts, " +
            "or after changing OAuth2 scopes. " +
            "Example: nr auth login --force")]
        bool force = false)
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();

        try
        {
            var result = await _authService.LoginAsync(credPath, tokenPath, force);
            var mode = _output.DetermineMode(globals, config);

            if (mode == OutputMode.Json)
                _output.WriteJson(result);
            else
                _output.WritePlain($"Authenticated successfully as {result.Email}");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed");
            _output.WriteError($"Login failed: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("logout", Description = "Revoke the stored OAuth2 token with Google and delete it locally. " +
        "After logging out, all Gmail commands will exit with code 3 until you run 'nr auth login' again. " +
        "If the revocation network call fails, the local token is still deleted and exit code is 0. " +
        "Example: nr auth logout")]
    public async Task LogoutAsync(GlobalOptions globals)
    {
        var config = _configLoader.Load(globals.Config);
        var tokenPath = AppPaths.GetTokenStorePath();

        try
        {
            await _authService.LogoutAsync(tokenPath);
            var mode = _output.DetermineMode(globals, config);

            if (mode == OutputMode.Json)
                _output.WriteJson(new { status = "logged_out" });
            else
                _output.WritePlain("Logged out. Token revoked.");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
    }

    [Command("status", Description = "Show current authentication state: account email, token expiry, and whether the token is valid. " +
        "Makes no network requests — reads only from the local token store. " +
        "Exit code: 0 if authenticated, 3 if not. " +
        "Useful as a pre-flight check in scripts: 'nr auth status || nr auth login'. " +
        "Example: nr auth status --json")]
    public async Task StatusAsync(GlobalOptions globals)
    {
        var config = _configLoader.Load(globals.Config);
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            var status = await _authService.GetStatusAsync(tokenPath);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(status);
            }
            else
            {
                _output.WriteKeyValue([
                    ("Authenticated", status.Authenticated.ToString()),
                    ("Account", status.Email ?? "(unknown)"),
                    ("Token expires", status.TokenExpiry.HasValue
                        ? $"{status.TokenExpiry.Value:u} ({(status.TokenValid ? "valid" : "invalid")})"
                        : "(n/a)")
                ], mode);
            }

            Environment.Exit(status.Authenticated ? ExitCodes.Success : ExitCodes.AuthRequired);
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
    }
}
