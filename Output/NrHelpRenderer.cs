using System.Reflection;
using System.Text;
using Cocona.Help;
using Cocona.Help.DocumentModel;

namespace NorthernRange.Output;

/// <summary>
/// Custom help renderer that:
/// 1. Prepends an app name/version banner to all help output.
/// 2. Inserts a blank line between consecutive option/command entries so
///    long descriptions don't visually run together.
/// </summary>
public class NrHelpRenderer : ICoconaHelpRenderer
{
    private readonly CoconaHelpRenderer _inner = new();

    public string Render(HelpMessage message)
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
        var version = raw.Split('+')[0];
        var header = $"northernrange v{version} — A read-only Gmail CLI for scripts, AI agents, and power users\n\n";

        var body = _inner.Render(message);
        body = AddEntrySpacing(body);

        return header + body;
    }

    // Insert a blank line between consecutive label-description list entries.
    // In Cocona's plain-text output, option and command entries are indented
    // with exactly 2 spaces (continuation lines would be indented further).
    private static string AddEntrySpacing(string text)
    {
        var lines = text.Split('\n');
        var sb = new StringBuilder(text.Length + lines.Length * 2);
        bool prevWasEntry = false;

        foreach (var line in lines)
        {
            // A label-description entry: "  --option" or "  subcommand"
            // (2 leading spaces, third character is non-space)
            bool isEntry = line.Length > 2 && line[0] == ' ' && line[1] == ' ' && line[2] != ' ';

            if (isEntry && prevWasEntry)
                sb.Append('\n');

            sb.Append(line).Append('\n');
            prevWasEntry = isEntry;
        }

        // Trim any trailing blank lines that may have been added
        var result = sb.ToString().TrimEnd('\n', '\r');
        return result + '\n';
    }
}
