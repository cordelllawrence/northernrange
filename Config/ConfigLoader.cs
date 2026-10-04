using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace NorthernRange.Config;

public class ConfigLoader
{
    // TypeInfoResolver is required: trimmed/single-file publish sets
    // JsonSerializerIsReflectionEnabledByDefault=false, which otherwise makes
    // Deserialize throw NotSupportedException on the first config read.
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

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
