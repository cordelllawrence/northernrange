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
    string? Config = null
) : ICommandParameterSet;
