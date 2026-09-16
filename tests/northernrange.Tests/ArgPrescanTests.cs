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

    [Fact]
    public void HasFlag_MatchesAnyAlias_AndEqualsForm()
    {
        Assert.True(ArgPrescan.HasFlag(["-v"], "--verbose", "-v"));
        Assert.True(ArgPrescan.HasFlag(["--log-format=text"], "--log-format"));
        Assert.False(ArgPrescan.HasFlag(["--log-file", "x"], "--log"));
    }
}
