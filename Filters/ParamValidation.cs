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

    /// <summary>
    /// Validates an enum-like option value case-insensitively and returns the
    /// canonical (lower-case) spelling. Exits 2 listing the allowed values otherwise.
    /// </summary>
    public static string RequireOneOf(string value, IReadOnlyList<string> allowed, string paramName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (allowed.Contains(normalized))
            return normalized;
        throw new NrException(ExitCodes.InvalidArguments,
            $"--{paramName} must be one of: {string.Join(", ", allowed)} (got '{value}').");
    }

    /// <summary>
    /// Flattens a repeatable option that also accepts comma-separated values:
    /// <c>--x a,b --x c</c> becomes <c>[a, b, c]</c>. Returns null when empty.
    /// </summary>
    public static string[]? SplitList(IEnumerable<string>? values)
    {
        if (values is null) return null;
        var items = values
            .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        return items.Length == 0 ? null : items;
    }
}
