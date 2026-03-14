using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Models;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class AuthCommands
{
    private readonly AuthService _authService;
    private readonly AccountResolver _resolver;
    private readonly ConfigPersister _configPersister;
    private readonly OutputWriter _output;
    private readonly ILogger<AuthCommands> _logger;

    public AuthCommands(
        AuthService authService,
        AccountResolver resolver,
        ConfigPersister configPersister,
        OutputWriter output,
        ILogger<AuthCommands> logger)
    {
        _authService = authService;
        _resolver = resolver;
        _configPersister = configPersister;
        _output = output;
        _logger = logger;
    }

    [Command("login", Description = "Authenticate with Gmail via the OAuth2 browser flow. Run once; token is stored for subsequent commands.")]
    public async Task LoginAsync(
        GlobalOptions globals,
        [Option("force", Description = "Delete the stored token and re-run the full browser consent flow.")]
        bool force = false)
    {
        var ctx = _resolver.Resolve(globals);

        try
        {
            var result = await _authService.LoginAsync(ctx.CredentialsPath, ctx.TokenStorePath, force);
            var mode = _output.DetermineMode(globals, ctx.Config);

            // Auto-create account entry in config on successful login
            if (_configPersister.EnsureAccount(ctx.Config, ctx.AccountName))
                _configPersister.Save(ctx.Config, globals.Config);

            var loginResult = new AuthLoginResult(result.Status, result.Email, ctx.AccountName);

            if (mode == OutputMode.Json)
                _output.WriteJson(loginResult);
            else
                _output.WritePlain($"Authenticated successfully as {result.Email} (account: {ctx.AccountName})");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for account {Account}", ctx.AccountName);
            _output.WriteError($"Login failed: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("logout", Description = "Revoke and delete the stored OAuth2 token.")]
    public async Task LogoutAsync(GlobalOptions globals)
    {
        var ctx = _resolver.Resolve(globals);

        try
        {
            await _authService.LogoutAsync(ctx.TokenStorePath);
            var mode = _output.DetermineMode(globals, ctx.Config);

            var result = new AuthLogoutResult("logged_out", ctx.AccountName);

            if (mode == OutputMode.Json)
                _output.WriteJson(result);
            else
                _output.WritePlain($"Logged out (account: {ctx.AccountName}). Token revoked.");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
    }

    [Command("status", Description = "Show authentication state. With --account: show one account. Without: show all accounts.")]
    public async Task StatusAsync(GlobalOptions globals)
    {
        var config = _resolver.LoadConfig(globals.Config);
        var mode = _output.DetermineMode(globals, config);

        try
        {
            // If a specific account was requested, show just that one
            if (globals.Account != null)
            {
                var ctx = _resolver.Resolve(globals);
                var status = await _authService.GetStatusAsync(ctx.TokenStorePath);

                if (mode == OutputMode.Json)
                {
                    _output.WriteJson(new AccountStatusEntry(
                        ctx.AccountName,
                        ctx.AccountName == (config.DefaultAccount ?? "default"),
                        status.Authenticated,
                        status.Email,
                        status.TokenExpiry,
                        status.TokenValid));
                }
                else
                {
                    WriteStatusPlain(ctx.AccountName, status, config, mode);
                }

                Environment.Exit(status.Authenticated ? ExitCodes.Success : ExitCodes.AuthRequired);
                return;
            }

            // No --account: show all configured accounts
            var accountNames = GetAllAccountNames(config);
            var entries = new List<AccountStatusEntry>();
            var anyAuthenticated = false;

            foreach (var name in accountNames)
            {
                var tokenPath = Path.Combine(AppPaths.GetTokenStorePath(), name);
                var status = await _authService.GetStatusAsync(tokenPath);
                var isDefault = name == (config.DefaultAccount ?? "default");

                entries.Add(new AccountStatusEntry(
                    name, isDefault, status.Authenticated, status.Email,
                    status.TokenExpiry, status.TokenValid));

                if (status.Authenticated) anyAuthenticated = true;
            }

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(new MultiAccountStatusResult(entries));
            }
            else
            {
                foreach (var entry in entries)
                {
                    var defaultTag = entry.IsDefault ? " (default)" : "";
                    _output.WriteKeyValue([
                        ("Account", $"{entry.Account}{defaultTag}"),
                        ("Authenticated", entry.Authenticated.ToString()),
                        ("Email", entry.Email ?? "(unknown)"),
                        ("Token expires", entry.TokenExpiry.HasValue
                            ? $"{entry.TokenExpiry.Value:u} ({(entry.TokenValid ? "valid" : "invalid")})"
                            : "(n/a)")
                    ], mode);
                    _output.WritePlain("");
                }
            }

            Environment.Exit(anyAuthenticated ? ExitCodes.Success : ExitCodes.AuthRequired);
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
    }

    private void WriteStatusPlain(string accountName, AuthStatusResult status, AppConfig config, OutputMode mode)
    {
        var isDefault = accountName == (config.DefaultAccount ?? "default");
        var defaultTag = isDefault ? " (default)" : "";

        _output.WriteKeyValue([
            ("Account", $"{accountName}{defaultTag}"),
            ("Authenticated", status.Authenticated.ToString()),
            ("Email", status.Email ?? "(unknown)"),
            ("Token expires", status.TokenExpiry.HasValue
                ? $"{status.TokenExpiry.Value:u} ({(status.TokenValid ? "valid" : "invalid")})"
                : "(n/a)")
        ], mode);
    }

    /// <summary>
    /// Collect all known account names: from config + check for "default" if not listed.
    /// Also discovers account subdirectories in the token store that aren't in config.
    /// </summary>
    private static List<string> GetAllAccountNames(AppConfig config)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (config.Accounts != null)
        {
            foreach (var key in config.Accounts.Keys)
                names.Add(key);
        }

        // Discover accounts from token subdirectories (handles accounts
        // that exist on disk but were removed from config)
        var tokenDir = AppPaths.GetTokenStorePath();
        if (Directory.Exists(tokenDir))
        {
            foreach (var dir in Directory.GetDirectories(tokenDir))
                names.Add(Path.GetFileName(dir));
        }

        // Ensure "default" is always present
        if (names.Count == 0)
            names.Add("default");

        return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
