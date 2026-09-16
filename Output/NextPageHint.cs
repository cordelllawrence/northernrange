using System.Text;
using NorthernRange.Commands;

namespace NorthernRange.Output;

/// <summary>
/// Builds the "Next page:" line printed after a paginated list in text mode.
/// A Gmail page token is only meaningful with the same filters that produced
/// it, so the hint must repeat every flag the user gave (label, query, page
/// size, format, account, config, credentials) before adding <c>--page-token</c>.
/// </summary>
public static class NextPageHint
{
    public static string Build(
        string command,
        string token,
        GlobalOptions? globals,
        params (string Flag, string? Value)[] flags)
    {
        var sb = new StringBuilder("Next page: nr ").Append(command);

        foreach (var (flag, value) in flags)
        {
            if (value is null) continue;
            sb.Append(' ').Append(flag).Append(' ').Append(Quote(value));
        }

        if (globals is not null)
        {
            if (globals.Account is not null)     sb.Append(" --account ").Append(Quote(globals.Account));
            if (globals.Config is not null)      sb.Append(" --config ").Append(Quote(globals.Config));
            if (globals.Credentials is not null) sb.Append(" --credentials ").Append(Quote(globals.Credentials));
        }

        sb.Append(" --page-token ").Append(token);
        return sb.ToString();
    }

    /// <summary>Wraps a value in double quotes when it needs them for a shell.</summary>
    public static string Quote(string value)
    {
        if (value.Length > 0 && !value.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '&' or '|' or '<' or '>' or '(' or ')'))
            return value;
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
