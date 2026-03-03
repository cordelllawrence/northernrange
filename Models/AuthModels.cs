namespace NorthernRange.Models;

public record AuthStatusResult(
    bool Authenticated,
    string? Email,
    DateTimeOffset? TokenExpiry,
    bool TokenValid);

public record AuthLoginResult(
    string Status,
    string? Email);

public record AuthLogoutResult(string Status);
