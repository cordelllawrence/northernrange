using System.Diagnostics;
using System.Text.Json;
using NorthernRange.Errors;
using Xunit;

namespace NorthernRange.Tests;

/// <summary>
/// Runs the real binary (copied into the test output by the project
/// reference) and checks the exit-code and stream contract for everything
/// that fails before any network call. This is the layer that catches
/// Cocona's own parse-error codes, which no unit test can reach.
/// </summary>
public class ExitCodeE2ETests
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "nr.exe" : "nr");

    private static async Task<(int Code, string Out, string Err)> Nr(string args, bool json = false)
    {
        // An isolated, non-existent config so the user's real config never leaks in.
        var cfg = Path.Combine(Path.GetTempPath(), "nr-e2e-" + Guid.NewGuid().ToString("N") + ".json");
        var psi = new ProcessStartInfo(Exe, $"--config \"{cfg}\" {args}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment.Remove("NR_JSON");
        if (json) psi.Environment["NR_JSON"] = "1";

        using var p = Process.Start(psi)!;
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        return (p.ExitCode, (await outTask).TrimEnd(), (await errTask).TrimEnd());
    }

    [Fact]
    public void Binary_IsPresent()
    {
        Assert.True(File.Exists(Exe), $"expected the app next to the tests at {Exe}");
    }

    [Fact]
    public async Task Help_ExitsZero_OnStdout()
    {
        var (code, stdout, stderr) = await Nr("--help");
        Assert.Equal(0, code);
        Assert.Contains("messages", stdout);
        Assert.Equal("", stderr);
    }

    [Fact]
    public async Task UnknownCommand_ExitsTwo()
    {
        var (code, stdout, stderr) = await Nr("frobnicate");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);
        Assert.Contains("not a command", stderr);
    }

    [Fact]
    public async Task UnknownOption_ExitsTwo_NotCoconas129()
    {
        var (code, stdout, stderr) = await Nr("messages list --bogus");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);
        Assert.Contains("Unknown option --bogus", stderr);
    }

    [Fact]
    public async Task BadIntegerValue_ExitsTwo_WithoutClrTypeNames()
    {
        var (code, stdout, stderr) = await Nr("messages list -n abc");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);
        Assert.Equal("Invalid value for --max: expected an integer.", stderr);
    }

    [Fact]
    public async Task MissingRequiredOption_ExitsTwo()
    {
        var (code, _, stderr) = await Nr("messages send -s hi");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("Missing required option --to.", stderr);
    }

    [Fact]
    public async Task MissingRequiredArgument_ExitsTwo()
    {
        var (code, _, stderr) = await Nr("messages read");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("Missing required argument <id>.", stderr);
    }

    [Fact]
    public async Task OutOfRangeValue_ExitsTwo()
    {
        var (code, _, stderr) = await Nr("messages list -n 501");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Contains("--max must be between 1 and 500", stderr);
    }

    [Fact]
    public async Task InvalidEnumValue_ExitsTwo_ListingAllowed()
    {
        var (code, _, stderr) = await Nr("messages read abc --format bogus");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("--format must be one of: full, metadata, minimal, raw (got 'bogus').", stderr);
    }

    [Fact]
    public async Task JsonMode_ErrorIsEnvelopeOnStderr_StdoutEmpty()
    {
        var (code, stdout, stderr) = await Nr("messages read abc --format bogus --json");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);

        using var doc = JsonDocument.Parse(stderr);
        var error = doc.RootElement.GetProperty("error");
        Assert.Equal(2, error.GetProperty("code").GetInt32());
        Assert.Contains("--format", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task JsonMode_ViaEnv_UnknownOptionIsEnvelope()
    {
        var (code, stdout, stderr) = await Nr("labels list --nope", json: true);
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal(2, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task GlobalOptions_AreAcceptedBeforeOrAfterTheCommand()
    {
        // Every call in this class already puts --config first. This one also
        // puts --json first and a global after, and expects the same envelope.
        var (code, stdout, stderr) = await Nr("--json messages read abc --format bogus --account default");
        Assert.Equal(ExitCodes.InvalidArguments, code);
        Assert.Equal("", stdout);
        using var doc = JsonDocument.Parse(stderr);
        Assert.Equal(2, doc.RootElement.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task LlmJson_IsValidJson_OnStdout_NothingOnStderr()
    {
        var (code, stdout, stderr) = await Nr("--llm --json");
        Assert.Equal(0, code);
        Assert.Equal("", stderr);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("nr", doc.RootElement.GetProperty("name").GetString());
    }
}
