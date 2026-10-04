using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Auth;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Models;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class AuthCommands
{
    private readonly AuthService _authService;
    private readonly CommandPrelude _prelude;
    private readonly ConfigPersister _configPersister;
    private readonly OutputWriter _output;
    private readonly ILogger<AuthCommands> _logger;

    public AuthCommands(
        AuthService authService,
        CommandPrelude prelude,
        ConfigPersister configPersister,
        OutputWriter output,
        ILogger<AuthCommands> logger)
    {
        _authService = authService;
        _prelude = prelude;
        _configPersister = configPersister;
        _output = output;
        _logger = logger;
    }

    [ErrorHandlingFilter]
    [Command("login", Description = "Authenticate with Gmail through the OAuth2 browser flow. Run once per account; the token is stored for later commands.")]
    public async Task LoginAsync(
        GlobalOptions globals,
        [Option("force", Description = "Delete the stored token and re-run the full browser consent flow.")]
        bool force = false)
    {
        using var session = _prelude.Begin(globals, _logger, "auth.login");

        var result = await _authService.LoginAsync(
            session.Account.CredentialsPath, session.Account.TokenStorePath, force);

        // Auto-create account entry in config on successful login
        if (_configPersister.EnsureAccount(session.Config, session.Account.AccountName))
            _configPersister.Save(session.Config, globals.Config);

        var loginResult = new AuthLoginResult(result.Status, result.Email, session.Account.AccountName);

        if (session.Mode == OutputMode.Json)
            _output.WriteJson(loginResult);
        else
            _output.WritePlain($"Authenticated successfully as {result.Email} (account: {session.Account.AccountName})");
    }

    [ErrorHandlingFilter]
    [Command("logout", Description = "Revoke the stored OAuth2 token with Google and delete it locally.")]
    public async Task LogoutAsync(GlobalOptions globals)
    {
        using var session = _prelude.Begin(globals, _logger, "auth.logout");

        await _authService.LogoutAsync(session.Account.TokenStorePath);

        var result = new AuthLogoutResult("logged_out", session.Account.AccountName);

        if (session.Mode == OutputMode.Json)
            _output.WriteJson(result);
        else
            _output.WritePlain($"Logged out (account: {session.Account.AccountName}). Token revoked.");
    }

    [ErrorHandlingFilter]
    [Command("status", Description = "Show authentication state for one account (--account) or all accounts. No network call.")]
    public async Task<int> StatusAsync(GlobalOptions globals)
    {
        // Status walks every known account when --account is absent, so it
        // can't use the account-bound prelude for the no-account branch.
        var (config, mode, scope) = _prelude.BeginConfigOnly(globals, _logger, "auth.status");
        using var _ = scope;

        // If a specific account was requested, show just that one
        if (globals.Account != null)
        {
            var ctx = _prelude.Resolver.Resolve(globals);
            var status = await _authService.GetStatusAsync(ctx.TokenStorePath);

            var entry = new AccountStatusEntry(
                ctx.AccountName,
                ctx.AccountName == (config.DefaultAccount ?? "default"),
                status.Authenticated,
                status.Email,
                status.TokenExpiry,
                status.TokenValid);

            if (mode == OutputMode.Json)
                _output.WriteJson(entry);
            else
                WriteStatusEntry(entry, mode);

            return status.Authenticated ? ExitCodes.Success : ExitCodes.AuthRequired;
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
                WriteStatusEntry(entry, mode);
                _output.WritePlain("");
            }
        }

        return anyAuthenticated ? ExitCodes.Success : ExitCodes.AuthRequired;
    }

    private void WriteStatusEntry(AccountStatusEntry entry, OutputMode mode)
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
