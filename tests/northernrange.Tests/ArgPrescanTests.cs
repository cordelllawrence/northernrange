using NorthernRange.Commands;
using Xunit;

namespace NorthernRange.Tests;

public class ArgPrescanTests
{
    [Fact]
    public void GetValue_SpaceSeparated()
    {
        Assert.Equal("x.jsonl", ArgPrescan.GetValue(["messages", "--log-file", "x.jsonl", "list"], "--log-file"));
    }

    [Fact]
    public void GetValue_EqualsForm()
    {
        Assert.Equal("x.jsonl", ArgPrescan.GetValue(["--log-file=x.jsonl", "--help"], "--log-file"));
    }

    [Fact]
    public void GetValue_Missing_ReturnsNull()
    {
        Assert.Null(ArgPrescan.GetValue(["--log"], "--log-file"));
        Assert.Null(ArgPrescan.GetValue(["--log-file"], "--log-file"));
    }

    [Fact]
    public void GetValue_DoesNotMatchPrefixOfLongerFlag()
    {
        // "--log" must not pick up "--log-file"'s value
        Assert.Null(ArgPrescan.GetValue(["--log-file", "x"], "--log"));
    }

    // ── HoistGlobalOptions ────────────────────────────────────────────────

    [Fact]
    public void Hoist_MovesLeadingGlobals_BehindTheCommand()
    {
        var result = ArgPrescan.HoistGlobalOptions(["--json", "--account", "work", "messages", "list", "-n", "5"]);
        Assert.Equal(["messages", "list", "-n", "5", "--json", "--account", "work"], result);
    }

    [Fact]
    public void Hoist_UnderstandsEqualsForm_AndShortVerbose()
    {
        var result = ArgPrescan.HoistGlobalOptions(["-v", "--config=c.json", "labels", "list"]);
        Assert.Equal(["labels", "list", "-v", "--config=c.json"], result);
    }

    [Fact]
    public void Hoist_LeavesArgsAlone_WhenNothingLeads()
    {
        string[] args = ["messages", "list", "--json"];
        Assert.Same(args, ArgPrescan.HoistGlobalOptions(args));
    }

    [Fact]
    public void Hoist_StopsAtUnknownOption_SoCoconaRejectsIt()
    {
        var result = ArgPrescan.HoistGlobalOptions(["--json", "--bogus", "messages", "list"]);
        Assert.Equal(["--bogus", "messages", "list", "--json"], result);
    }

    [Fact]
    public void Hoist_DropsGlobals_WhenOnlyHelpFollows()
    {
        Assert.Equal(["--help"], ArgPrescan.HoistGlobalOptions(["--config", "x.json", "--help"]));
        Assert.Empty(ArgPrescan.HoistGlobalOptions(["--json"]));
    }

    [Fact]
    public void Hoist_DoesNotTouchHelpOrVersion()
    {
        Assert.Equal(["--help"], ArgPrescan.HoistGlobalOptions(["--help"]));
        Assert.Equal(["--version"], ArgPrescan.HoistGlobalOptions(["--version"]));
    }

    [Fact]
    public void HasFlag_MatchesAnyAlias_AndEqualsForm()
    {
        Assert.True(ArgPrescan.HasFlag(["-v"], "--verbose", "-v"));
        Assert.True(ArgPrescan.HasFlag(["--log-format=text"], "--log-format"));
        Assert.False(ArgPrescan.HasFlag(["--log-file", "x"], "--log"));
    }
}
