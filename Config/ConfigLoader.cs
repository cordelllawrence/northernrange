using System.Text.Json;

namespace NorthernRange.Config;

public class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public AppConfig Load(string? configPathOverride = null)
    {
        var path = configPathOverride
            ?? Environment.GetEnvironmentVariable("NR_CONFIG")
            ?? AppPaths.GetConfigFilePath();

        var config = new AppConfig();

        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (loaded != null) config = loaded;
            }
            catch
            {
                // Malformed config — use defaults, don't crash
            }
        }

        // Environment variable overrides (higher precedence than file)
        var envLabel = Environment.GetEnvironmentVariable("NR_DEFAULT_LABEL");
        if (!string.IsNullOrEmpty(envLabel)) config.DefaultLabel = envLabel;

        var envMax = Environment.GetEnvironmentVariable("NR_MAX_RESULTS");
        if (int.TryParse(envMax, out var maxResults) && maxResults is >= 1 and <= 500)
            config.DefaultMaxResults = maxResults;

        var envCreds = Environment.GetEnvironmentVariable("NR_CREDENTIALS");
        if (!string.IsNullOrEmpty(envCreds)) config.CredentialsPath = envCreds;

        if (Environment.GetEnvironmentVariable("NR_JSON") == "1")
            config.DefaultOutputFormat = "json";

        return config;
    }
}
