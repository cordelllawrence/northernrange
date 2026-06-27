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
}
