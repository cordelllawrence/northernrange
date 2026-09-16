namespace NorthernRange.Errors;

/// <summary>
/// Process exit codes. These are a documented contract for scripts and agents;
/// change them only with a matching change to USAGE.md, README.md and the
/// tables in <c>LlmDocGenerator</c>.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralError = 1;
    public const int InvalidArguments = 2;
    public const int AuthRequired = 3;
    public const int ApiError = 4;
    public const int NotFound = 5;
    public const int FileError = 6;
    /// <summary>Interrupted (Ctrl+C). 128 + SIGINT, the shell convention.</summary>
    public const int Cancelled = 130;
}
