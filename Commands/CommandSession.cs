using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

/// <summary>
/// A command's resolved environment: account/paths/config, chosen
/// <see cref="OutputMode"/>, and a lazily-opened Gmail client. Produced by
/// <see cref="CommandPrelude.Begin"/>; disposing the session closes the log
/// scope opened at the same time.
/// </summary>
public sealed class CommandSession : IDisposable
{
    private readonly GmailClientFactory _factory;
    private readonly IDisposable? _logScope;
    private GmailService? _gmail;

    internal CommandSession(ResolvedContext account, OutputMode mode, IDisposable? logScope, GmailClientFactory factory)
    {
        Account = account;
        Mode = mode;
        _logScope = logScope;
        _factory = factory;
    }

    public ResolvedContext Account { get; }
    public AppConfig Config => Account.Config;
    public OutputMode Mode { get; }

    public async Task<GmailService> GmailAsync()
    {
        _gmail ??= await _factory.GetServiceAsync(
            Account.CredentialsPath, Account.TokenStorePath, Account.Config.HttpTimeoutSeconds);
        return _gmail;
    }

    public void Dispose() => _logScope?.Dispose();
}

/// <summary>
/// Shared command prologue: resolves the account, decides the output mode,
/// opens a scoped log context, and hands the Gmail client over on request.
/// Every command method begins with <c>using var session = _prelude.Begin(...)</c>
/// instead of repeating the four prologue lines by hand.
/// </summary>
public class CommandPrelude
{
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly GmailClientFactory _gmailFactory;

    public CommandPrelude(AccountResolver resolver, OutputWriter output, GmailClientFactory gmailFactory)
    {
        _resolver = resolver;
        _output = output;
        _gmailFactory = gmailFactory;
    }

    /// <summary>
    /// Resolve the account for <paramref name="globals"/>, pick the output
    /// mode, and open a log scope tagged with <paramref name="command"/> plus
    /// any extra key/value pairs.
    /// </summary>
    public CommandSession Begin(
        GlobalOptions globals,
        ILogger logger,
        string command,
        params (string Key, object Value)[] scope)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);
        return new CommandSession(ctx, mode, OpenScope(logger, command, scope), _gmailFactory);
    }

    /// <summary>
    /// Load config without picking an account. For commands like
    /// <c>auth status</c> that must enumerate every account before picking
    /// one. Returns a session whose <c>Account</c> is <c>null</c>-equivalent:
    /// callers that touch it get a resolver-driven resolution per account.
    /// </summary>
    public (AppConfig Config, OutputMode Mode, IDisposable? LogScope) BeginConfigOnly(
        GlobalOptions globals,
        ILogger logger,
        string command,
        params (string Key, object Value)[] scope)
    {
        var config = _resolver.LoadConfig(globals.Config);
        var mode = _output.DetermineMode(globals, config);
        return (config, mode, OpenScope(logger, command, scope));
    }

    public AccountResolver Resolver => _resolver;

    private static IDisposable? OpenScope(ILogger logger, string command, (string Key, object Value)[] extras)
    {
        var state = new Dictionary<string, object>(extras.Length + 1) { ["Command"] = command };
        foreach (var (k, v) in extras)
            state[k] = v;
        return logger.BeginScope(state);
    }
}
