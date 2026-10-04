using NorthernRange.Errors;
using NorthernRange.Gmail;
using Xunit;

namespace NorthernRange.Tests;

// Address and attachment validation live below the command layer so every
// send/reply path benefits from the same checks. The errors must come back
// as NrException(InvalidArguments) so agents see exit 2 and a clear
// message, not an unhandled ParseException.
public class SendServiceValidationTests
{
    [Theory]
    [InlineData("alice@example.com", "alice@example.com")]
    [InlineData("Alice <alice@example.com>", "alice@example.com")]
    [InlineData("\"Alice, Jr.\" <alice@example.com>", "alice@example.com")]
    [InlineData("a.b+tag@example.co.uk", "a.b+tag@example.co.uk")]
    public void ParseAddressList_AcceptsValidAddresses(string input, string expectedAddress)
    {
        var parsed = SendService.ParseAddressList(new[] { input }, "--to");
        Assert.Single(parsed);
        Assert.Equal(expectedAddress, parsed[0].Address, ignoreCase: true);
    }

    [Theory]
    [InlineData("not an email")]
    [InlineData("@example.com")]
    [InlineData("alice@")]
    [InlineData("alice@localhost")]  // no dotted domain, rejected
    [InlineData("oops")]              // bare token MimeKit otherwise accepts
    [InlineData("")]
    [InlineData("   ")]
    public void ParseAddressList_RejectsInvalid_WithExit2(string address)
    {
        var ex = Assert.Throws<NrException>(
            () => SendService.ParseAddressList(new[] { address }, "--to"));
        Assert.Equal(ExitCodes.InvalidArguments, ex.ExitCode);
        Assert.Contains("--to", ex.Message);
    }

    [Fact]
    public void ParseAddressList_NamesTheOffendingFlag()
    {
        var ex = Assert.Throws<NrException>(
            () => SendService.ParseAddressList(new[] { "no-at-sign" }, "--cc"));
        Assert.Contains("--cc", ex.Message);
    }

    [Fact]
    public void ParseAddressList_StopsAtFirstInvalid()
    {
        // Second entry is bad; the error should name it, not "ok@x.com".
        var ex = Assert.Throws<NrException>(
            () => SendService.ParseAddressList(new[] { "ok@x.com", "nope" }, "--to"));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void AttachmentLimit_Is20MiB_AndMatchesReality()
    {
        // Pinned so a future tightening or loosening is a conscious change.
        Assert.Equal(20L * 1024 * 1024, SendService.GmailAttachmentLimitBytes);
    }
}
