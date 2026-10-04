using Cocona;

namespace NorthernRange.Commands;

public record GlobalOptions(
    [Option(FlagNames.Json, Description = "Output machine-readable JSON to stdout. Also enabled by NR_JSON=1.")]
    bool Json = false,

    [Option(FlagNames.Ui, Description = "Rich tables and key-value blocks on a terminal; body text stays plain. Ignored when stdout is redirected.")]
    bool Ui = false,

    [Option(FlagNames.VerboseShort, Description = "Emit debug diagnostics to stderr. Never affects stdout.")]
    bool Verbose = false,

    [Option(FlagNames.Credentials, Description = "Path to client_secrets.json. Overrides config and the default location.")]
    string? Credentials = null,

    [Option(FlagNames.Config, Description = "Path to config.json. Overrides the default location.")]
    string? Config = null,

    [Option(FlagNames.Account, Description = "Account name to use. Overrides NR_ACCOUNT env var and config defaultAccount.")]
    string? Account = null,

    [Option(FlagNames.Log, Description = "Write a log file (nr-YYYYMMDD.jsonl or .log) in the current directory. See --log-format and --log-file.")]
    bool Log = false,

    [Option(FlagNames.LogFormat, Description = "Log file format: jsonl (default) or text.")]
    string LogFormat = "jsonl",

    [Option(FlagNames.LogFile, Description = "Write the log to this path instead (appends if it exists). Implies --log.")]
    string? LogFile = null,

    [Option(FlagNames.LogLevel, Description = "Minimum log level: verbose, debug, information (default), warning, error, fatal.")]
    string? LogLevel = null
) : ICommandParameterSet;
