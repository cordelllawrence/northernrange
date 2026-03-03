using System.Text;
using System.Text.RegularExpressions;
using Google.Apis.Gmail.v1.Data;
using NorthernRange.Models;

namespace NorthernRange.Mime;

public partial class MimeParser
{
    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"<(style|script|head|title)(\s[^>]*)?>[\s\S]*?</(style|script|head|title)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockElementPattern();

    public MessageBody? ParseBody(MessagePart? payload)
    {
        if (payload is null) return null;

        // Simple single-part message
        if (payload.MimeType == "text/plain" && payload.Body?.Data is not null)
            return new MessageBody("text/plain", DecodeBase64UrlToString(payload.Body.Data));

        if (payload.MimeType == "text/html" && payload.Body?.Data is not null)
            return new MessageBody("text/html", StripHtml(DecodeBase64UrlToString(payload.Body.Data)));

        // Multipart — recurse, prefer text/plain
        if (payload.Parts is { Count: > 0 })
        {
            var plain = FindPartRecursive(payload.Parts, "text/plain");
            if (plain?.Body?.Data is not null)
                return new MessageBody("text/plain", DecodeBase64UrlToString(plain.Body.Data));

            var html = FindPartRecursive(payload.Parts, "text/html");
            if (html?.Body?.Data is not null)
                return new MessageBody("text/html", StripHtml(DecodeBase64UrlToString(html.Body.Data)));
        }

        return null;
    }

    public List<AttachmentInfo> ExtractAttachments(MessagePart? payload)
    {
        var results = new List<AttachmentInfo>();
        if (payload is not null)
            CollectAttachments(payload, results);
        return results;
    }

    private static void CollectAttachments(MessagePart part, List<AttachmentInfo> results)
    {
        if (!string.IsNullOrEmpty(part.Filename) && !string.IsNullOrEmpty(part.Body?.AttachmentId))
        {
            results.Add(new AttachmentInfo(
                part.Body!.AttachmentId!,
                part.Filename!,
                part.MimeType ?? "application/octet-stream",
                part.Body.Size ?? 0));
        }

        if (part.Parts is null) return;
        foreach (var child in part.Parts)
            CollectAttachments(child, results);
    }

    private static MessagePart? FindPartRecursive(IList<MessagePart>? parts, string mimeType)
    {
        if (parts is null) return null;
        foreach (var part in parts)
        {
            if (part.MimeType == mimeType) return part;
            var found = FindPartRecursive(part.Parts, mimeType);
            if (found is not null) return found;
        }
        return null;
    }

    private static string StripHtml(string html)
    {
        // Remove block elements whose content should not appear as text
        var noBlocks = BlockElementPattern().Replace(html, " ");
        var decoded = System.Net.WebUtility.HtmlDecode(noBlocks);
        var stripped = HtmlTagPattern().Replace(decoded, " ");
        return Regex.Replace(stripped, @"\s{2,}", " ").Trim();
    }

    public static string DecodeBase64UrlToString(string base64url) =>
        Encoding.UTF8.GetString(DecodeBase64Url(base64url));

    public static byte[] DecodeBase64Url(string base64url)
    {
        var s = base64url.Replace('-', '+').Replace('_', '/');
        var pad = s.Length % 4;
        if (pad != 0) s += new string('=', 4 - pad);
        return Convert.FromBase64String(s);
    }

    public static Dictionary<string, string> ParseHeaders(IList<MessagePartHeader>? headers)
    {
        if (headers is null) return [];
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            if (h.Name is not null)
                dict[h.Name] = h.Value ?? "";
        return dict;
    }

    public static DateTimeOffset ParseInternalDate(long? internalDateMs) =>
        internalDateMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(internalDateMs.Value)
            : DateTimeOffset.UtcNow;
}
