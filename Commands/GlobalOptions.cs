using Cocona;

namespace NorthernRange.Commands;

public record GlobalOptions(
    [Option("json", Description = "Output machine-readable JSON to stdout. Disables all rich rendering and ANSI codes. " +
        "The stable output contract for scripts and AI agents. " +
        "Also enabled by setting NR_JSON=1 in the environment.")]
    bool Json = false,

    [Option("ui", Description = "Enable Spectre.Console rich rendering: colored tables, styled text. " +
        "Ignored automatically if stdout is redirected (e.g. piped to a file) or if --json is also set.")]
    bool Ui = false,

    [Option('v', Description = "Emit debug-level diagnostic output to stderr. " +
        "Shows API calls, token refresh events, and resolved paths. " +
        "Never affects stdout — safe to use alongside --json.")]
    bool Verbose = false,

    [Option("credentials", Description = "Path to your client_secrets.json file (OAuth2 Desktop client ID " +
        "downloaded from Google Cloud Console → APIs & Services → Credentials). " +
        "Overrides the config file setting and the default location " +
        "(%APPDATA%\\northernrange\\client_secrets.json on Windows, " +
        "~/.config/northernrange/client_secrets.json on macOS/Linux). " +
        "Example: --credentials ~/secrets/my-project-client.json")]
    string? Credentials = null,

    [Option("config", Description = "Path to a northernrange config.json file. " +
        "Default: %APPDATA%\\northernrange\\config.json (Windows) or ~/.config/northernrange/config.json (macOS/Linux). " +
        "The config file sets defaults for --label, --max, output format, date format, and more. " +
        "Example: --config ~/projects/nr-work-config.json")]
    string? Config = null
) : ICommandParameterSet;
