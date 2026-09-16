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

    [Option("log", Description = "Write a log file (nr-YYYYMMDD.jsonl or .log) in the current directory. See --log-format and --log-file.")]
    bool Log = false,

    [Option("log-format", Description = "Log file format: jsonl (default) or text.")]
    string LogFormat = "jsonl",

    [Option("log-file", Description = "Write the log to this path instead (appends if it exists). Implies --log.")]
    string? LogFile = null,

    [Option("log-level", Description = "Minimum log level: verbose, debug, information (default), warning, error, fatal.")]
    string? LogLevel = null
) : ICommandParameterSet;
