namespace NorthernRange.Config;

public class AccountConfig
{
    /// <summary>
    /// Override path to client_secrets.json for this account.
    /// Null means use the global default.
    /// </summary>
    public string? CredentialsPath { get; set; }
}
