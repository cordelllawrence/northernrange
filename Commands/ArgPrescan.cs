namespace NorthernRange.Commands;

/// <summary>
/// Minimal argument scan used before the Cocona host exists, so logging can be
/// configured from the command line. Understands <c>--flag value</c> and
/// <c>--flag=value</c>. Flag names here must match <see cref="GlobalOptions"/>.
/// </summary>
public static class ArgPrescan
{
    public static bool HasFlag(string[] args, params string[] names) =>
        args.Any(a => names.Contains(a) || names.Any(n => a.StartsWith(n + "=", StringComparison.Ordinal)));

    public static string? GetValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name)
                return i + 1 < args.Length ? args[i + 1] : null;
            if (args[i].StartsWith(name + "=", StringComparison.Ordinal))
                return args[i][(name.Length + 1)..];
        }
        return null;
    }
}
