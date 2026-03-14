namespace NorthernRange.Config;

public class AppConfig
{
    public string DefaultLabel { get; set; } = "INBOX";
    public int DefaultMaxResults { get; set; } = 25;
    /// <summary>"text" or "json"</summary>
    public string DefaultOutputFormat { get; set; } = "text";
    /// <summary>"iso8601" or "local"</summary>
    public string DateFormat { get; set; } = "iso8601";
    public string? CredentialsPath { get; set; }
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>Account name used when --account is not specified. Defaults to "default".</summary>
    public string? DefaultAccount { get; set; }

    /// <summary>Named accounts. Each key is an account alias (e.g. "work", "personal").</summary>
    public Dictionary<string, AccountConfig>? Accounts { get; set; }
}
