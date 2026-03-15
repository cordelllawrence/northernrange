using NorthernRange.Errors;

namespace NorthernRange.Filters;

public static class ParamValidation
{
    public static void RequireRange(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
            throw new NrException(ExitCodes.InvalidArguments,
                $"--{paramName} must be between {min} and {max} (got {value}).");
    }
}
