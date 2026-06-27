using System.Text;
using Google.Apis.Gmail.v1.Data;
using NorthernRange.Mime;
using Xunit;

namespace NorthernRange.Tests;

public class MimeParserTests
{
    private readonly MimeParser _parser = new();

    // Gmail uses URL-safe base64 without padding.
    private static string B64Url(string text)
    {
        var b = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return b.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    // ── base64url decoding ────────────────────────────────────────────────

    [Theory]
    [InlineData("hello")]
    [InlineData("")]
    [InlineData("a")]            // 1 byte → 2 padding chars
    [InlineData("ab")]           // 2 bytes → 1 padding char
    [InlineData("abc")]          // 3 bytes → no padding
    [InlineData("Iñtërnâtiônàlizætiøn ☃ 漢字")]
    public void DecodeBase64Url_RoundTrips(string original)
    {
        var decoded = MimeParser.DecodeBase64UrlToString(B64Url(original));
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void DecodeBase64Url_HandlesUrlSafeAlphabet()
    {
        // 0xFB 0xFF encodes to "+/" in standard base64 and "-_" in url-safe.
        var bytes = MimeParser.DecodeBase64Url("-_8");  // no padding supplied
        Assert.Equal(new byte[] { 0xFB, 0xFF }, bytes);
    }

    // ── header parsing ────────────────────────────────────────────────────

    [Fact]
    public void ParseHeaders_IsCaseInsensitive()
    {
        var headers = MimeParser.ParseHeaders(new List<MessagePartHeader>
        {
            new() { Name = "From", Value = "alice@example.com" },
            new() { Name = "Subject", Value = "Hi" },
        });

        Assert.Equal("alice@example.com", headers["from"]);
        Assert.Equal("Hi", headers["SUBJECT"]);
    }

    [Fact]
    public void ParseHeaders_NullInput_ReturnsEmpty()
    {
        Assert.Empty(MimeParser.ParseHeaders(null));
    }

    // ── internal date ─────────────────────────────────────────────────────

    [Fact]
    public void ParseInternalDate_ConvertsUnixMillis()
    {
        // 2026-03-01T00:00:00Z = 1772323200000 ms
        var dt = MimeParser.ParseInternalDate(1772323200000);
        Assert.Equal(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), dt);
    }

    // ── body extraction ───────────────────────────────────────────────────

    [Fact]
    public void ParseBody_PrefersPlainText_InSinglePart()
    {
        var payload = new MessagePart
        {
            MimeType = "text/plain",
            Body = new MessagePartBody { Data = B64Url("Hello world") }
        };

        var body = _parser.ParseBody(payload);
        Assert.NotNull(body);
        Assert.Equal("text/plain", body!.MimeType);
        Assert.Equal("Hello world", body.Text);
    }

    [Fact]
    public void ParseBody_Multipart_PrefersPlainOverHtml()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/alternative",
            Parts = new List<MessagePart>
            {
                new() { MimeType = "text/html", Body = new MessagePartBody { Data = B64Url("<p>HTML</p>") } },
                new() { MimeType = "text/plain", Body = new MessagePartBody { Data = B64Url("PLAIN") } },
            }
        };

        var body = _parser.ParseBody(payload);
        Assert.Equal("text/plain", body!.MimeType);
        Assert.Equal("PLAIN", body.Text);
    }

    [Fact]
    public void ParseBody_FallsBackToHtml_StrippedToText()
    {
        var payload = new MessagePart
        {
            MimeType = "text/html",
            Body = new MessagePartBody
            {
                Data = B64Url("<html><head><style>p{color:red}</style></head><body><p>Hi&nbsp;there</p></body></html>")
            }
        };

        var body = _parser.ParseBody(payload);
        Assert.Equal("text/html", body!.MimeType);
        // Script/style/head content removed, tags stripped, entities decoded.
        Assert.DoesNotContain("color:red", body.Text);
        Assert.DoesNotContain("<", body.Text);
        Assert.Contains("Hi", body.Text);
        Assert.Contains("there", body.Text);
    }

    [Fact]
    public void ParseBody_NestedMultipart_FindsPlainRecursively()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Parts = new List<MessagePart>
            {
                new()
                {
                    MimeType = "multipart/alternative",
                    Parts = new List<MessagePart>
                    {
                        new() { MimeType = "text/plain", Body = new MessagePartBody { Data = B64Url("deep plain") } }
                    }
                }
            }
        };

        Assert.Equal("deep plain", _parser.ParseBody(payload)!.Text);
    }

    [Fact]
    public void ParseBody_NullPayload_ReturnsNull()
    {
        Assert.Null(_parser.ParseBody(null));
    }

    // ── attachment extraction ─────────────────────────────────────────────

    [Fact]
    public void ExtractAttachments_ReturnsPartsWithFilenameAndAttachmentId()
    {
        var payload = new MessagePart
        {
            MimeType = "multipart/mixed",
            Parts = new List<MessagePart>
            {
                new() { MimeType = "text/plain", Body = new MessagePartBody { Data = B64Url("body") } },
                new()
                {
                    MimeType = "application/pdf",
                    Filename = "report.pdf",
                    Body = new MessagePartBody { AttachmentId = "att-1", Size = 1234 }
                },
            }
        };

        var atts = _parser.ExtractAttachments(payload);
        var att = Assert.Single(atts);
        Assert.Equal("att-1", att.AttachmentId);
        Assert.Equal("report.pdf", att.Filename);
        Assert.Equal("application/pdf", att.MimeType);
        Assert.Equal(1234, att.Size);
    }

    [Fact]
    public void ExtractAttachments_SkipsInlinePartsWithoutAttachmentId()
    {
        // Inline image referenced only by Content-ID (no attachmentId) must not be listed.
        var payload = new MessagePart
        {
            MimeType = "multipart/related",
            Parts = new List<MessagePart>
            {
                new() { MimeType = "image/png", Filename = "logo.png", Body = new MessagePartBody { Size = 50 } },
            }
        };

        Assert.Empty(_parser.ExtractAttachments(payload));
    }

    [Fact]
    public void ExtractAttachments_NullPayload_ReturnsEmpty()
    {
        Assert.Empty(_parser.ExtractAttachments(null));
    }
}
