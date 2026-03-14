using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace NorthernRange.Config;

public class ConfigPersister
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    /// <summary>
    /// Writes the config to disk at the resolved config path.
    /// </summary>
    public void Save(AppConfig config, string? configPathOverride = null)
    {
        var path = configPathOverride
            ?? Environment.GetEnvironmentVariable("NR_CONFIG")
            ?? AppPaths.GetConfigFilePath();

        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, JsonOpts);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Ensures an account entry exists in the config. Returns true if config was modified.
    /// Sets the account as default if it is the first one added.
    /// </summary>
    public bool EnsureAccount(AppConfig config, string accountName, AccountConfig? accountConfig = null)
    {
        config.Accounts ??= new Dictionary<string, AccountConfig>();

        if (config.Accounts.ContainsKey(accountName))
            return false;

        config.Accounts[accountName] = accountConfig ?? new AccountConfig();

        if (config.DefaultAccount == null)
            config.DefaultAccount = accountName;

        return true;
    }
}
