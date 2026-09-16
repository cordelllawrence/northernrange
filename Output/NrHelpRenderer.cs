using System.Reflection;
using System.Text;
using Cocona.Help;
using Cocona.Help.DocumentModel;

namespace NorthernRange.Output;

/// <summary>
/// Custom help renderer that:
/// 1. Prepends an app name/version banner to all help output.
/// 2. Appends global options and special flags to root-level help.
/// </summary>
public class NrHelpRenderer : ICoconaHelpRenderer
{
    private readonly CoconaHelpRenderer _inner = new();

    public string Render(HelpMessage message)
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.1.0";
        var version = raw.Split('+')[0];
        var header = $"northernrange v{version} — A Gmail CLI for scripts, AI agents, and power users\n\n";

        var body = _inner.Render(message);

        bool isRootHelp = message.Children.OfType<HelpSection>()
            .Any(s => s.Id == HelpSectionId.Commands);

        if (isRootHelp)
            body += RootHelpExtras();

        return header + body;
    }

    private static string RootHelpExtras()
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("Global Options:");
        sb.AppendLine("  --json                    Output machine-readable JSON to stdout");
        sb.AppendLine("  --ui                      Enable Spectre.Console rich rendering");
        sb.AppendLine("  -v, --verbose             Emit debug diagnostics to stderr");
        sb.AppendLine("  --credentials <path>      Path to client_secrets.json");
        sb.AppendLine("  --config <path>           Path to config.json");
        sb.AppendLine("  --account <name>          Account name to use (overrides NR_ACCOUNT and config)");
        sb.AppendLine("  --log                     Write a log file (nr-YYYYMMDD.jsonl or .log) in the current directory");
        sb.AppendLine("  --log-format <fmt>        Log file format: jsonl (default) or text");
        sb.AppendLine("  --log-file <path>         Write the log to this path instead (appends; implies --log)");
        sb.AppendLine("  --log-level <level>       Minimum log level (verbose|debug|information|warning|error|fatal)");
        sb.AppendLine();
        sb.AppendLine("Documentation:");
        sb.AppendLine("  --llm                     Print concise AI-consumable documentation (Markdown)");
        sb.AppendLine("  --llm-full                Print full documentation with JSON schemas (Markdown)");
        sb.AppendLine("  --llm --json              Print documentation as a JSON tool schema");
        sb.AppendLine("  --llm [group] [command]   Filter documentation by command group or subcommand");

        return sb.ToString();
    }

}
