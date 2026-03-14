using NorthernRange.Commands;

namespace NorthernRange.Config;

/// <summary>
/// Resolved paths and config for a specific account.
/// </summary>
public record ResolvedContext(
    string AccountName,
    string CredentialsPath,
    string TokenStorePath,
    AppConfig Config);

/// <summary>
/// Centralises account resolution so every command can replace its 3-line
/// boilerplate with a single <c>_resolver.Resolve(globals)</c> call.
/// <para>Precedence: --account flag > NR_ACCOUNT env > config.DefaultAccount > "default"</para>
/// </summary>
public class AccountResolver
{
    private readonly ConfigLoader _configLoader;

    public AccountResolver(ConfigLoader configLoader)
    {
        _configLoader = configLoader;
    }

    public ResolvedContext Resolve(GlobalOptions globals)
        => Resolve(globals.Config, globals.Account, globals.Credentials);

    public ResolvedContext Resolve(
        string? configPathOverride,
        string? accountOverride,
        string? credentialsOverride)
    {
        var config = _configLoader.Load(configPathOverride);

        var accountName = accountOverride
            ?? Environment.GetEnvironmentVariable("NR_ACCOUNT")
            ?? config.DefaultAccount
            ?? "default";

        AccountConfig? accountConfig = null;
        config.Accounts?.TryGetValue(accountName, out accountConfig);

        var credPath = credentialsOverride
            ?? accountConfig?.CredentialsPath
            ?? config.CredentialsPath
            ?? AppPaths.GetClientSecretsPath();

        var tokenPath = Path.Combine(AppPaths.GetTokenStorePath(), accountName);

        return new ResolvedContext(accountName, credPath, tokenPath, config);
    }

    /// <summary>
    /// Load just the config (without resolving an account). Useful for commands
    /// that need to enumerate all accounts (e.g. <c>auth status</c> with no --account).
    /// </summary>
    public AppConfig LoadConfig(string? configPathOverride)
        => _configLoader.Load(configPathOverride);
}
