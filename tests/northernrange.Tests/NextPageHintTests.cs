using NorthernRange.Commands;
using NorthernRange.Output;
using Xunit;

namespace NorthernRange.Tests;

/// <summary>
/// Regression for the confirmed pager defect: the "Next page:" hint dropped
/// every filter, so page 2 ran under a different query and page size.
/// </summary>
public class NextPageHintTests
{
    [Fact]
    public void Build_RepeatsEveryGivenFilter_BeforeToken()
    {
        var hint = NextPageHint.Build("messages list", "TOK", null,
            ("--label", "Work/Projects"), ("--query", "is:unread"), ("--max", "2"), ("--format", "minimal"));

        Assert.Equal("Next page: nr messages list --label Work/Projects --query is:unread --max 2 --format minimal --page-token TOK", hint);
    }

    [Fact]
    public void Build_OmitsFlagsThatWereNotGiven()
    {
        var hint = NextPageHint.Build("threads list", "TOK", null,
            ("--label", null), ("--query", null), ("--max", null));

        Assert.Equal("Next page: nr threads list --page-token TOK", hint);
    }

    [Fact]
    public void Build_QuotesValuesWithSpaces_AndEscapesQuotes()
    {
        var hint = NextPageHint.Build("messages list", "TOK", null,
            ("--query", "from:alice@example.com subject:\"Q3 report\""));

        Assert.Equal("Next page: nr messages list --query \"from:alice@example.com subject:\\\"Q3 report\\\"\" --page-token TOK", hint);
    }

    [Fact]
    public void Build_IncludesAccountConfigAndCredentials_WhenGiven()
    {
        var globals = new GlobalOptions(Account: "work", Config: "C:\\cfg\\my config.json", Credentials: "/tmp/c.json");

        var hint = NextPageHint.Build("drafts list", "TOK", globals);

        Assert.Equal("Next page: nr drafts list --account work --config \"C:\\\\cfg\\\\my config.json\" --credentials /tmp/c.json --page-token TOK", hint);
    }

    [Fact]
    public void Build_IgnoresGlobalsThatWereNotGiven()
    {
        var hint = NextPageHint.Build("drafts list", "TOK", new GlobalOptions());

        Assert.Equal("Next page: nr drafts list --page-token TOK", hint);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("a&b", "\"a&b\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("", "\"\"")]
    public void Quote_OnlyWhenNeeded(string input, string expected)
    {
        Assert.Equal(expected, NextPageHint.Quote(input));
    }
}
