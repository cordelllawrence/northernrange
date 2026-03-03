using System.Text;
using Cocona;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using NorthernRange.Auth;
using NorthernRange.Commands;
using NorthernRange.Config;
using NorthernRange.Gmail;
using NorthernRange.Mime;
using Cocona.Help;
using NorthernRange.Output;

// UTF-8 must be set before any output
Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

// Pre-scan raw args for Serilog configuration before host builds
var isVerbose = args.Any(a => a is "--verbose" or "-v");
var isJson = args.Contains("--json") || Environment.GetEnvironmentVariable("NR_JSON") == "1";

// Ensure config/log/token directories exist
AppPaths.EnsureDirectoriesExist();

// Bootstrap Serilog before host build so startup errors are captured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Google", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: Path.Combine(AppPaths.GetLogDir(), "nr-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}{NewLine}{Properties:j}{NewLine}")
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
    .CreateLogger();

try
{
    Log.Information("northernrange starting. Verbose={Verbose}, Json={Json}", isVerbose, isJson);
    Log.Debug("Config dir: {ConfigDir}", AppPaths.GetConfigDir());

    await CoconaApp.CreateHostBuilder()
        .ConfigureLogging(lb =>
        {
            lb.ClearProviders();
            lb.AddSerilog(Log.Logger, dispose: true);
        })
        .ConfigureServices(services =>
        {
            services.AddSingleton<ConfigLoader>();
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
        })
        .RunAsync<NorthernRangeApp>(args);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.Exit(1);
}
finally
{
    await Log.CloseAndFlushAsync();
}
