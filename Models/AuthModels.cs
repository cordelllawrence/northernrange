namespace NorthernRange.Models;

public record AuthStatusResult(
    bool Authenticated,
    string? Email,
    DateTimeOffset? TokenExpiry,
    bool TokenValid);

public record AuthLoginResult(
    string Status,
    string? Email,
    string? Account = null);

public record AuthLogoutResult(
    string Status,
    string? Account = null);

public record AccountStatusEntry(
    string Account,
    bool IsDefault,
    bool Authenticated,
    string? Email,
    DateTimeOffset? TokenExpiry,
    bool TokenValid);

public record MultiAccountStatusResult(
    List<AccountStatusEntry> Accounts);
