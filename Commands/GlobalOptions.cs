using Cocona;

namespace NorthernRange.Commands;

public record GlobalOptions(
    [Option("json", Description = "Output machine-readable JSON to stdout")]
    bool Json = false,

    [Option("ui", Description = "Enable rich terminal output (requires a TTY)")]
    bool Ui = false,

    [Option('v', Description = "Emit additional diagnostic output to stderr")]
    bool Verbose = false,

    [Option("credentials", Description = "Path to client_secrets.json")]
    string? Credentials = null,

    [Option("config", Description = "Path to northernrange config file")]
    string? Config = null
) : ICommandParameterSet;
