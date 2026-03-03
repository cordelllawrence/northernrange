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

    [Command("login", Description = "Authenticate with Google via OAuth2 browser flow")]
    public async Task LoginAsync(
        GlobalOptions globals,
        [Option("force", Description = "Re-run the browser flow even if already authenticated")]
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

    [Command("logout", Description = "Revoke stored token and sign out")]
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

    [Command("status", Description = "Show current authentication state")]
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
