using System.Text;
using Cocona;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using NorthernRange.Auth;
using NorthernRange.Commands;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Mime;
using Cocona.Help;
using NorthernRange.Output;

// UTF-8 must be set before any output. Guarded: setting the console encoding
// throws when stdin/stdout is redirected to a non-console handle (pipes, files).
try
{
    Console.OutputEncoding = Encoding.UTF8;
    Console.InputEncoding = Encoding.UTF8;
}
catch (IOException) { /* redirected handle — encoding stays at the host default */ }

// Pre-scan raw args for Serilog configuration before the host exists. Flag
// names must match GlobalOptions; ArgPrescan handles both "--x v" and "--x=v".
var isVerbose = ArgPrescan.HasFlag(args, "--verbose", "-v");
var isJson = ArgPrescan.HasFlag(args, "--json") || EnvVars.JsonRequested();
ErrorOutput.JsonMode = isJson;

var logFile = ArgPrescan.GetValue(args, "--log-file");
var isLog = ArgPrescan.HasFlag(args, "--log") || logFile is not null;
var logFormat = (ArgPrescan.GetValue(args, "--log-format") ?? "jsonl").ToLowerInvariant();
var isLogText = logFormat == "text";
var logLevelStr = ArgPrescan.GetValue(args, "--log-level");

// LLM documentation — handled before host build (no DI, no auth needed)
var isLlm = args.Contains("--llm");
var isLlmFull = args.Contains("--llm-full");

if (isLlm || isLlmFull)
{
    // Remaining args after stripping flags become an optional filter (e.g. "messages", "send reply")
    var filter = args.Where(a => a is not "--llm" and not "--llm-full" and not "--json").ToArray();

    if (isJson)
        Console.WriteLine(LlmDocGenerator.GenerateJsonToolSchema(filter));
    else if (isLlmFull)
        Console.WriteLine(LlmDocGenerator.GenerateFullMarkdown(filter));
    else
        Console.WriteLine(LlmDocGenerator.GenerateConciseMarkdown(filter));
    return;
}

// Ensure config/log/token directories exist
AppPaths.EnsureDirectoriesExist();

// Bootstrap Serilog before host build so startup errors are captured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Google", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    // No always-on file sink: the tool writes nothing to disk unless asked
    // (--log / --log-file). Warnings and errors still reach stderr below.
    .WriteTo.Conditional(
        _ => isVerbose && !isJson,
        wt => wt.Console(
            standardErrorFromLevel: LogEventLevel.Verbose,
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .WriteTo.Conditional(
        _ => !isVerbose && !isJson,
        wt => wt.Console(
            standardErrorFromLevel: LogEventLevel.Warning,
            restrictedToMinimumLevel: LogEventLevel.Warning,
            outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .WriteTo.Conditional(
        _ => isLog && !isLogText,
        wt => wt.File(
            formatter: new JsonlLogFormatter(),
            path: logFile ?? Path.Combine(Environment.CurrentDirectory, "nr-.jsonl"),
            rollingInterval: logFile is not null ? RollingInterval.Infinite : RollingInterval.Day,
            restrictedToMinimumLevel: ParseLogLevel(logLevelStr),
            shared: true))
    .WriteTo.Conditional(
        _ => isLog && isLogText,
        wt => wt.File(
            path: logFile ?? Path.Combine(Environment.CurrentDirectory, "nr-.log"),
            rollingInterval: logFile is not null ? RollingInterval.Infinite : RollingInterval.Day,
            restrictedToMinimumLevel: ParseLogLevel(logLevelStr),
            shared: true,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}{Properties:j}{NewLine}"))
    .CreateLogger();

try
{
    if (isLog)
    {
        var ext = isLogText ? ".log" : ".jsonl";
        var resolvedPath = logFile ?? Path.GetFullPath($"nr-{DateTime.Now:yyyyMMdd}{ext}");
        Console.Error.WriteLine($"log: {resolvedPath}");
    }

    Log.Information("northernrange starting. Verbose={Verbose}, Json={Json}", isVerbose, isJson);
    Log.Debug("Config dir: {ConfigDir}", AppPaths.GetConfigDir());

    // Let global options appear before the command as well as after it.
    args = ArgPrescan.HoistGlobalOptions(args);

    await CoconaApp.CreateHostBuilder()
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(Log.Logger, dispose: true);
        })
        .ConfigureServices(services =>
        {
            services.AddSingleton<ConfigLoader>();
            services.AddSingleton<ConfigPersister>();
            services.AddSingleton<AccountResolver>();
            services.AddSingleton<AuthService>();
            services.AddSingleton<GmailClientFactory>();
            services.AddSingleton<MimeParser>();
            services.AddSingleton<MessageService>();
            services.AddSingleton<ThreadService>();
            services.AddSingleton<LabelService>();
            services.AddSingleton<AttachmentService>();
            services.AddSingleton<OutputWriter>();
            services.AddSingleton<SendService>();
            services.AddSingleton<ICoconaHelpRenderer, NrHelpRenderer>();
            NrDispatchPipeline.Register(services);
        })
        .RunAsync<NorthernRangeApp>(args);

    // Cocona reports an unknown command as exit 1 before any command runs.
    // The documented contract is 2 for anything wrong on the command line.
    if (Environment.ExitCode == ExitCodes.GeneralError && !NrDispatchPipeline.Dispatched)
        Environment.ExitCode = ExitCodes.InvalidArguments;

    // Cocona exits 129 after printing help that was asked for. Help is a
    // success. (Unknown options, Cocona's other 129, are handled by
    // NrDispatchPipeline before this point.)
    if (Environment.ExitCode == 129 && ArgPrescan.HasFlag(args, "--help", "-h"))
        Environment.ExitCode = ExitCodes.Success;
}
catch (OperationCanceledException)
{
    ErrorOutput.Write(ExitCodes.Cancelled, "Cancelled.");
    Environment.ExitCode = ExitCodes.Cancelled;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    ErrorOutput.Write(ExitCodes.GeneralError, $"Unexpected error: {ex.Message}");
    Environment.ExitCode = ExitCodes.GeneralError;
}
finally
{
    await Log.CloseAndFlushAsync();
}

static LogEventLevel ParseLogLevel(string? level) => level?.ToLowerInvariant() switch
{
    "verbose" => LogEventLevel.Verbose,
    "debug" => LogEventLevel.Debug,
    "warning" => LogEventLevel.Warning,
    "error" => LogEventLevel.Error,
    "fatal" => LogEventLevel.Fatal,
    _ => LogEventLevel.Information,
};
