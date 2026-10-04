namespace NorthernRange.Commands;

/// <summary>
/// Canonical option names, in one place. Cocona's <c>[Option]</c> attribute
/// takes the bare name (no leading dashes); <c>Program.cs</c> and
/// <see cref="ArgPrescan"/> match the full <c>--name</c> form before the
/// host exists. Keeping both as <c>const</c> on this class lets the compiler
/// catch typos and makes any rename a single-file change.
/// </summary>
public static class FlagNames
{
    // Bare option names, used in [Option("...")] attributes on GlobalOptions.
    public const string Json       = "json";
    public const string Ui         = "ui";
    public const string Verbose    = "verbose";
    public const string Credentials = "credentials";
    public const string Config     = "config";
    public const string Account    = "account";
    public const string Log        = "log";
    public const string LogFormat  = "log-format";
    public const string LogFile    = "log-file";
    public const string LogLevel   = "log-level";

    // Non-GlobalOptions flags that still need pre-host matching.
    public const string Llm        = "llm";
    public const string LlmFull    = "llm-full";
    public const string Help       = "help";

    // Short names.
    public const char VerboseShort = 'v';
    public const char HelpShort    = 'h';

    // Full --name forms, built from the bare names so there is still one
    // source of truth for the spelling. These are the strings handed to
    // ArgPrescan before Cocona exists.
    public const string JsonLong       = "--" + Json;
    public const string UiLong         = "--" + Ui;
    public const string VerboseLong    = "--" + Verbose;
    public const string VerboseShortArg = "-v"; // char constants can't concat with strings in attributes
    public const string LogLong        = "--" + Log;
    public const string LogFormatLong  = "--" + LogFormat;
    public const string LogFileLong    = "--" + LogFile;
    public const string LogLevelLong   = "--" + LogLevel;
    public const string LlmLong        = "--" + Llm;
    public const string LlmFullLong    = "--" + LlmFull;
    public const string HelpLong       = "--" + Help;
    public const string HelpShortArg   = "-h";
}
