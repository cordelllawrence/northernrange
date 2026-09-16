namespace NorthernRange.Config;

/// <summary>
/// Names of the environment variables the tool honours, plus the one parsing
/// rule they share. Keep this the only place that spells them out.
/// </summary>
public static class EnvVars
{
    public const string Json = "NR_JSON";
    public const string Account = "NR_ACCOUNT";
    public const string Credentials = "NR_CREDENTIALS";
    public const string Config = "NR_CONFIG";
    public const string DefaultLabel = "NR_DEFAULT_LABEL";
    public const string MaxResults = "NR_MAX_RESULTS";

    /// <summary>True for "1", "true", or "yes" (case-insensitive, trimmed).</summary>
    public static bool IsTruthy(string? value) =>
        value?.Trim().ToLowerInvariant() is "1" or "true" or "yes";

    public static bool JsonRequested() =>
        IsTruthy(Environment.GetEnvironmentVariable(Json));
}
