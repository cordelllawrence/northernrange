using NorthernRange.Gmail;
using Xunit;

namespace NorthernRange.Tests;

public class SendServiceReplyTests
{
    private static Dictionary<string, string> Headers(params (string, string)[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in pairs) d[k] = v;
        return d;
    }

    [Fact]
    public void BuildReplyFields_RepliesToOriginalSender_NotReplyAll()
    {
        var (to, cc, subject, _) = SendService.BuildReplyFields(
            Headers(("From", "alice@example.com"), ("To", "me@example.com"), ("Subject", "Lunch")),
            replyAll: false);

        Assert.Equal(new[] { "alice@example.com" }, to);
        Assert.Null(cc);                       // no reply-all → no Cc
        Assert.Equal("Re: Lunch", subject);
    }

    [Fact]
    public void BuildReplyFields_AddsRePrefixOnlyOnce()
    {
        var (_, _, subject, _) = SendService.BuildReplyFields(
            Headers(("From", "a@x.com"), ("Subject", "Re: Lunch")), replyAll: false);

        Assert.Equal("Re: Lunch", subject);     // not "Re: Re: Lunch"
    }

    [Fact]
    public void BuildReplyFields_RePrefix_IsCaseInsensitive()
    {
        var (_, _, subject, _) = SendService.BuildReplyFields(
            Headers(("From", "a@x.com"), ("Subject", "RE: Lunch")), replyAll: false);

        Assert.Equal("RE: Lunch", subject);
    }

    [Fact]
    public void BuildReplyFields_ReplyAll_CcsOriginalToAndCc()
    {
        var (to, cc, _, _) = SendService.BuildReplyFields(
            Headers(
                ("From", "alice@example.com"),
                ("To", "me@example.com, bob@example.com"),
                ("Cc", "carol@example.com")),
            replyAll: true);

        Assert.Equal(new[] { "alice@example.com" }, to);
        Assert.NotNull(cc);
        Assert.Contains("me@example.com", cc!);
        Assert.Contains("bob@example.com", cc);
        Assert.Contains("carol@example.com", cc);
    }

    [Fact]
    public void BuildReplyFields_BuildsReferencesChain()
    {
        var (_, _, _, references) = SendService.BuildReplyFields(
            Headers(("From", "a@x.com"), ("Message-ID", "<m2@x>"), ("References", "<m0@x> <m1@x>")),
            replyAll: false);

        Assert.Equal("<m0@x> <m1@x> <m2@x>", references);
    }

    [Fact]
    public void BuildReplyFields_References_FallsBackToMessageId_WhenNoChain()
    {
        var (_, _, _, references) = SendService.BuildReplyFields(
            Headers(("From", "a@x.com"), ("Message-ID", "<only@x>")), replyAll: false);

        Assert.Equal("<only@x>", references);
    }

    [Fact]
    public void BuildReplyFields_ReplyTo_OverridesFrom()
    {
        // Mailing lists set Reply-To so replies go to the list, not the sender.
        var (to, _, _, _) = SendService.BuildReplyFields(
            Headers(("From", "alice@example.com"), ("Reply-To", "list@example.com"), ("Subject", "Topic")),
            replyAll: false);

        Assert.Equal(new[] { "list@example.com" }, to);
    }

    [Fact]
    public void BuildReplyFields_NoReplyTo_FallsBackToFrom()
    {
        var (to, _, _, _) = SendService.BuildReplyFields(
            Headers(("From", "alice@example.com"), ("Subject", "Topic")), replyAll: false);

        Assert.Equal(new[] { "alice@example.com" }, to);
    }

    [Fact]
    public void BuildReplyFields_EmptyReplyTo_FallsBackToFrom()
    {
        // A whitespace-only Reply-To must not shadow a valid From.
        var (to, _, _, _) = SendService.BuildReplyFields(
            Headers(("From", "alice@example.com"), ("Reply-To", "   "), ("Subject", "Topic")),
            replyAll: false);

        Assert.Equal(new[] { "alice@example.com" }, to);
    }

    [Fact]
    public void BuildReplyFields_ReplyAll_DoesNotCcPrimaryRecipient()
    {
        // alice is the primary (From) and must not also appear in Cc.
        var (to, cc, _, _) = SendService.BuildReplyFields(
            Headers(
                ("From", "alice@example.com"),
                ("To", "alice@example.com, bob@example.com"),
                ("Cc", "carol@example.com")),
            replyAll: true);

        Assert.Equal(new[] { "alice@example.com" }, to);
        Assert.NotNull(cc);
        Assert.DoesNotContain("alice@example.com", cc!);
        Assert.Contains("bob@example.com", cc);
        Assert.Contains("carol@example.com", cc);
    }

    [Fact]
    public void BuildReplyFields_ReplyAll_DedupIgnoresDisplayNames()
    {
        // Primary derives from Reply-To as a bare address; original To uses
        // a display name wrapping the same address. Dedup by email, not text.
        var (to, cc, _, _) = SendService.BuildReplyFields(
            Headers(
                ("From", "list-owner@x.com"),
                ("Reply-To", "list@example.com"),
                ("To", "\"The List\" <list@example.com>, bob@example.com")),
            replyAll: true);

        Assert.Equal(new[] { "list@example.com" }, to);
        Assert.NotNull(cc);
        Assert.DoesNotContain(cc!, c => SendService.ExtractEmailAddress(c) == "list@example.com");
        Assert.Contains("bob@example.com", cc);
    }

    [Fact]
    public void BuildReplyFields_ReplyAll_WithOnlyPrimaryInOrig_YieldsNullCc()
    {
        // Everyone in the original To was the primary; after dedup, Cc is
        // empty, which we normalise to null so the message has no empty Cc.
        var (to, cc, _, _) = SendService.BuildReplyFields(
            Headers(("From", "alice@example.com"), ("To", "alice@example.com")),
            replyAll: true);

        Assert.Equal(new[] { "alice@example.com" }, to);
        Assert.Null(cc);
    }

    [Theory]
    [InlineData("alice@example.com", "alice@example.com")]
    [InlineData("Alice <alice@example.com>", "alice@example.com")]
    [InlineData("\"Alice Example\" <alice@example.com>", "alice@example.com")]
    [InlineData("  bob@example.com  ", "bob@example.com")]
    public void ExtractEmailAddress_StripsDisplayAndWhitespace(string input, string expected)
    {
        Assert.Equal(expected, SendService.ExtractEmailAddress(input));
    }
}
