using Cocona;

namespace NorthernRange.Commands;

public record GlobalOptions(
    [Option("json", Description = "Output machine-readable JSON to stdout. Also enabled by NR_JSON=1.")]
    bool Json = false,

    [Option("ui", Description = "Enable Spectre.Console rich rendering (auto-disabled when stdout is redirected).")]
    bool Ui = false,

    [Option('v', Description = "Emit debug diagnostics to stderr. Never affects stdout.")]
    bool Verbose = false,

    [Option("credentials", Description = "Path to client_secrets.json. Overrides config and the default location.")]
    string? Credentials = null,

    [Option("config", Description = "Path to config.json. Overrides the default location.")]
    string? Config = null,

    [Option("account", Description = "Account name to use. Overrides NR_ACCOUNT env var and config defaultAccount.")]
    string? Account = null,

    [Option("log", Description = "Enable JSONL debug logging to a timestamped file in the current directory.")]
    bool Log = false,

    [Option("log-flat", Description = "Enable structured text logging to a timestamped file in the current directory.")]
    bool LogFlat = false,

    [Option("log-file", Description = "Write log to this path (appends if exists). Format follows --log or --log-flat.")]
    string? LogFile = null,

    [Option("log-level", Description = "Minimum log level: verbose, debug, information (default), warning, error.")]
    string? LogLevel = null
) : ICommandParameterSet;
