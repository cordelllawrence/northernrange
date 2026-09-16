using System.Reflection;
using Cocona;

namespace NorthernRange.Commands;

/// <summary>
/// Argument handling that has to happen before the Cocona host exists.
/// Understands <c>--flag value</c> and <c>--flag=value</c>. Flag names are
/// read from <see cref="GlobalOptions"/> so there is one source of truth.
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

    /// <summary>
    /// Cocona binds global options per command, so it only accepts them after
    /// the subcommand: <c>nr messages list --json</c> works, <c>nr --json messages
    /// list</c> does not. This moves any leading global options behind the
    /// command so both forms work. Unknown leading options are left alone for
    /// Cocona to reject. If only <c>--help</c> / <c>--version</c> follows, the
    /// globals are dropped: they have no meaning there.
    /// </summary>
    public static string[] HoistGlobalOptions(string[] args)
    {
        var specs = GlobalOptionSpecs.Value;
        var hoisted = new List<string>();
        var i = 0;

        while (i < args.Length && args[i].StartsWith('-') && args[i].Length > 1)
        {
            var eq = args[i].IndexOf('=');
            var name = eq < 0 ? args[i] : args[i][..eq];
            if (!specs.TryGetValue(name, out var takesValue))
                break;

            hoisted.Add(args[i]);
            i++;
            if (takesValue && eq < 0 && i < args.Length)
                hoisted.Add(args[i++]);
        }

        if (hoisted.Count == 0)
            return args;

        var rest = args[i..];
        if (rest.Length == 0 || rest[0] is "--help" or "-h" or "--version")
            return rest;

        return [.. rest, .. hoisted];
    }

    /// <summary>Global option spellings ("--json", "-v", …) mapped to whether they take a value.</summary>
    private static readonly Lazy<Dictionary<string, bool>> GlobalOptionSpecs = new(() =>
    {
        var specs = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var p in typeof(GlobalOptions).GetConstructors()[0].GetParameters())
        {
            var opt = p.GetCustomAttribute<OptionAttribute>();
            if (opt is null) continue;

            var takesValue = p.ParameterType != typeof(bool);
            var longName = opt.Name ?? ToKebabCase(p.Name!);
            specs["--" + longName] = takesValue;
            foreach (var s in opt.ShortNames)
                specs["-" + s] = takesValue;
        }
        return specs;
    });

    private static string ToKebabCase(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
