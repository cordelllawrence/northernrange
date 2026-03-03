namespace NorthernRange.Errors;

public class NrException : Exception
{
    public int ExitCode { get; }

    public NrException(int exitCode, string message) : base(message)
    {
        ExitCode = exitCode;
    }

    public NrException(int exitCode, string message, Exception inner) : base(message, inner)
    {
        ExitCode = exitCode;
    }
}
