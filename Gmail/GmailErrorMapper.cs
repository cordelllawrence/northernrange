using NorthernRange.Errors;

namespace NorthernRange.Gmail;

/// <summary>
/// Single translation point from <see cref="Google.GoogleApiException"/> to
/// <see cref="NrException"/> (message + exit code), shared by every Gmail
/// service so status-code handling can't drift between them.
/// </summary>
internal static class GmailErrorMapper
{
    /// <summary>
    /// Maps a Gmail API exception to an <see cref="NrException"/>.
    /// </summary>
    /// <param name="ex">The API exception.</param>
    /// <param name="notFoundMessage">
    /// Optional resource-specific message for 404s (e.g. "Message 'x' not found.").
    /// When null, a generic not-found message is used.
    /// </param>
    public static NrException Map(Google.GoogleApiException ex, string? notFoundMessage = null) =>
        ex.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.NotFound =>
                new NrException(ExitCodes.NotFound,
                    notFoundMessage ?? $"Resource not found: {Detail(ex)}"),
            System.Net.HttpStatusCode.Unauthorized =>
                new NrException(ExitCodes.AuthRequired,
                    "Authentication expired. Run 'nr auth login'."),
            System.Net.HttpStatusCode.Forbidden =>
                new NrException(ExitCodes.ApiError,
                    $"Access denied (check OAuth scope / permissions): {Detail(ex)}"),
            _ =>
                new NrException(ExitCodes.ApiError,
                    $"Gmail API error ({(int)ex.HttpStatusCode}): {Detail(ex)}")
        };

    private static string Detail(Google.GoogleApiException ex) => ex.Error?.Message ?? ex.Message;
}
