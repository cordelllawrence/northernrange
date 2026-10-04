using NorthernRange.Commands;
using NorthernRange.Config;
using NorthernRange.Output;
using Xunit;

namespace NorthernRange.Tests;

// Covers OutputWriter.DetermineMode including the --ui auto-disable-on-redirect
// branch. `Console.IsOutputRedirected` is a cross-platform BCL property (uses
// isatty on POSIX, GetFileType on Windows); the tests here feed both values of
// that signal directly so the behavior is verified on every platform the test
// suite runs on, closing the Phase 2 "verify on Linux" straggler.
public class OutputWriterModeTests
{
    private static readonly GlobalOptions Defaults = new();

    private static readonly string JsonEnv = EnvVars.Json;

    private static OutputWriter NewWriter() => new();

    private static (OutputMode mode, string stderr) Run(GlobalOptions globals, AppConfig config, bool isRedirected)
    {
        var sw = new StringWriter();
        var mode = NewWriter().DetermineMode(globals, config, isRedirected, sw);
        return (mode, sw.ToString());
    }

    private static IDisposable ClearJsonEnv()
    {
        var prior = Environment.GetEnvironmentVariable(JsonEnv);
        Environment.SetEnvironmentVariable(JsonEnv, null);
        return new EnvRestore(JsonEnv, prior);
    }

    [Fact]
    public void Default_IsPlainText_OnTty()
    {
        using var _ = ClearJsonEnv();
        var (mode, err) = Run(Defaults, new AppConfig(), isRedirected: false);
        Assert.Equal(OutputMode.PlainText, mode);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void Default_IsPlainText_WhenRedirected()
    {
        using var _ = ClearJsonEnv();
        var (mode, err) = Run(Defaults, new AppConfig(), isRedirected: true);
        Assert.Equal(OutputMode.PlainText, mode);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void UiFlag_OnTty_SelectsRichUi()
    {
        using var _ = ClearJsonEnv();
        var globals = Defaults with { Ui = true };
        var (mode, err) = Run(globals, new AppConfig(), isRedirected: false);
        Assert.Equal(OutputMode.RichUi, mode);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void UiFlag_WhenRedirected_FallsBackToPlainTextAndWarnsOnStderr()
    {
        using var _ = ClearJsonEnv();
        var globals = Defaults with { Ui = true };
        var (mode, err) = Run(globals, new AppConfig(), isRedirected: true);
        Assert.Equal(OutputMode.PlainText, mode);
        Assert.Contains("--ui flag ignored", err);
        Assert.Contains("stdout is redirected", err);
    }

    [Fact]
    public void JsonFlag_BeatsUi_RegardlessOfRedirect()
    {
        using var _ = ClearJsonEnv();
        var globals = Defaults with { Json = true, Ui = true };
        var (mode, err) = Run(globals, new AppConfig(), isRedirected: false);
        Assert.Equal(OutputMode.Json, mode);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void ConfigDefaultOutputFormatJson_SelectsJson()
    {
        using var _ = ClearJsonEnv();
        var config = new AppConfig { DefaultOutputFormat = "json" };
        var (mode, err) = Run(Defaults, config, isRedirected: false);
        Assert.Equal(OutputMode.Json, mode);
        Assert.Equal(string.Empty, err);
    }

    [Fact]
    public void NrJsonEnv_SelectsJson()
    {
        var prior = Environment.GetEnvironmentVariable(JsonEnv);
        Environment.SetEnvironmentVariable(JsonEnv, "1");
        try
        {
            var (mode, err) = Run(Defaults, new AppConfig(), isRedirected: false);
            Assert.Equal(OutputMode.Json, mode);
            Assert.Equal(string.Empty, err);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JsonEnv, prior);
        }
    }

    private sealed class EnvRestore : IDisposable
    {
        private readonly string _name;
        private readonly string? _value;
        public EnvRestore(string name, string? value) { _name = name; _value = value; }
        public void Dispose() => Environment.SetEnvironmentVariable(_name, _value);
    }
}
