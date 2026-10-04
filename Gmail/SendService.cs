using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Microsoft.Extensions.Logging;
using MimeKit;
using NorthernRange.Errors;
using NorthernRange.Models;
using NrMimeParser = NorthernRange.Mime.MimeParser;

namespace NorthernRange.Gmail;

/// <summary>
/// Composes and sends mail and manages drafts. Builds RFC 2822 messages with
/// MimeKit (base64url-encoded into <c>Message.Raw</c>), sets reply threading
/// headers (<c>In-Reply-To</c>/<c>References</c>), and wraps
/// <c>users.messages.send</c> and <c>users.drafts</c>.
/// </summary>
public class SendService
{
    private readonly ILogger<SendService> _logger;

    public SendService(ILogger<SendService> logger)
    {
        _logger = logger;
    }

    // ── Send new message ─────────────────────────────────────────────────────

    public async Task<SendResult> SendNewAsync(
        GmailService gmail,
        IList<string> to,
        IList<string>? cc,
        IList<string>? bcc,
        string subject,
        string body,
        IList<string>? attachmentPaths,
        bool asDraft,
        CancellationToken ct = default)
    {
        _logger.LogInformation("SendNew. To={To}, Subject={Subject}, Draft={Draft}",
            string.Join(", ", to), subject, asDraft);

        var rawMsg = await BuildRawMessageAsync(to, cc, bcc, subject, body, attachmentPaths);

        try
        {
            if (asDraft)
            {
                var created = await gmail.Users.Drafts
                    .Create(new Draft { Message = rawMsg }, "me")
                    .ExecuteAsync(ct);
                return new SendResult(
                    created.Message?.Id ?? "",
                    created.Message?.ThreadId,
                    subject,
                    to.ToList(),
                    IsDraft: true,
                    created.Id);
            }
            else
            {
                var sent = await gmail.Users.Messages
                    .Send(rawMsg, "me")
                    .ExecuteAsync(ct);
                return new SendResult(sent.Id, sent.ThreadId, subject, to.ToList(), IsDraft: false, null);
            }
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }
    }

    // ── Reply to a message ───────────────────────────────────────────────────

    public async Task<SendResult> SendReplyAsync(
        GmailService gmail,
        string replyToMessageId,
        string body,
        IList<string>? attachmentPaths,
        bool replyAll,
        bool asDraft,
        CancellationToken ct = default)
    {
        _logger.LogInformation("SendReply. ReplyTo={Id}, ReplyAll={ReplyAll}, Draft={Draft}",
            replyToMessageId, replyAll, asDraft);

        // Fetch original message to get threading headers
        Message original;
        try
        {
            var req = gmail.Users.Messages.Get("me", replyToMessageId);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            req.MetadataHeaders = new Google.Apis.Util.Repeatable<string>(
                ["From", "Reply-To", "To", "Cc", "Subject", "Message-ID", "References"]);
            original = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }

        var headers = NrMimeParser.ParseHeaders(original.Payload?.Headers);
        headers.TryGetValue("Message-ID", out var origMessageId);
        var (to, cc, replySubject, references) = BuildReplyFields(headers, replyAll);

        var rawMsg = await BuildRawMessageAsync(
            to, cc, null, replySubject, body, attachmentPaths,
            inReplyTo: origMessageId, references: references);

        rawMsg.ThreadId = original.ThreadId;

        try
        {
            if (asDraft)
            {
                var created = await gmail.Users.Drafts
                    .Create(new Draft { Message = rawMsg }, "me")
                    .ExecuteAsync(ct);
                return new SendResult(
                    created.Message?.Id ?? "",
                    original.ThreadId,
                    replySubject,
                    to,
                    IsDraft: true,
                    created.Id);
            }
            else
            {
                var sent = await gmail.Users.Messages
                    .Send(rawMsg, "me")
                    .ExecuteAsync(ct);
                return new SendResult(sent.Id, sent.ThreadId, replySubject, to, IsDraft: false, null);
            }
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }
    }

    // ── Draft management ─────────────────────────────────────────────────────

    public async Task<DraftListResult> ListDraftsAsync(
        GmailService gmail,
        int maxResults,
        string? pageToken = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ListDrafts. Max={Max}", maxResults);

        ListDraftsResponse listResp;
        try
        {
            var listReq = gmail.Users.Drafts.List("me");
            listReq.MaxResults = maxResults;
            listReq.PageToken = pageToken;
            listResp = await listReq.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }

        if (listResp.Drafts is null || listResp.Drafts.Count == 0)
            return new DraftListResult([], listResp.NextPageToken, listResp.ResultSizeEstimate ?? 0);

        // Fetch metadata for each draft in parallel. Task.WhenAll preserves the
        // input order, and the list keeps Gmail's order: re-sorting inside a
        // page would interleave with the next page's order.
        var summaries = await Task.WhenAll(listResp.Drafts.Select(d =>
            FetchDraftSummaryAsync(gmail, d.Id, ct)));

        return new DraftListResult(
            [.. summaries],
            listResp.NextPageToken,
            listResp.ResultSizeEstimate ?? summaries.Length);
    }

    private static async Task<DraftSummary> FetchDraftSummaryAsync(
        GmailService gmail, string draftId, CancellationToken ct)
    {
        try
        {
            var req = gmail.Users.Drafts.Get("me", draftId);
            req.Format = UsersResource.DraftsResource.GetRequest.FormatEnum.Metadata;
            var draft = await req.ExecuteAsync(ct);

            var headers = NrMimeParser.ParseHeaders(draft.Message?.Payload?.Headers);
            headers.TryGetValue("Subject", out var subject);
            headers.TryGetValue("To", out var to);

            DateTimeOffset? date = null;
            if (headers.TryGetValue("Date", out var dateStr) && !string.IsNullOrEmpty(dateStr))
                if (MimeKit.Utils.DateUtils.TryParse(dateStr, out var parsed)) date = parsed;

            return new DraftSummary(draftId, draft.Message?.Id, subject, to, draft.Message?.Snippet, date);
        }
        catch
        {
            return new DraftSummary(draftId, null, null, null, null, null);
        }
    }

    public async Task<SendResult> SendDraftAsync(
        GmailService gmail,
        string draftId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("SendDraft. DraftId={DraftId}", draftId);

        try
        {
            // Get draft metadata for the result
            var getReq = gmail.Users.Drafts.Get("me", draftId);
            getReq.Format = UsersResource.DraftsResource.GetRequest.FormatEnum.Metadata;
            var draft = await getReq.ExecuteAsync(ct);

            var headers = NrMimeParser.ParseHeaders(draft.Message?.Payload?.Headers);
            headers.TryGetValue("Subject", out var subject);
            headers.TryGetValue("To", out var toStr);
            var toList = toStr?.Split(',', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim()).ToList() ?? [];

            // Send the draft
            var sent = await gmail.Users.Drafts
                .Send(new Draft { Id = draftId }, "me")
                .ExecuteAsync(ct);

            return new SendResult(sent.Id, sent.ThreadId, subject ?? "", toList, IsDraft: false, null);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }
    }

    public async Task DeleteDraftAsync(
        GmailService gmail,
        string draftId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("DeleteDraft. DraftId={DraftId}", draftId);

        try
        {
            await gmail.Users.Drafts.Delete("me", draftId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }
    }

    // ── Reply field computation (pure; unit-tested) ──────────────────────────

    /// <summary>
    /// Derives reply recipients, subject, and the References chain from the
    /// original message's headers. The reply goes to the <c>Reply-To</c>
    /// address if present, otherwise the <c>From</c> address (RFC 5322 §3.6.2).
    /// With <paramref name="replyAll"/>, the original To + Cc become the new
    /// Cc, with the primary recipient deduped out. The subject gains a single
    /// "Re: " prefix (not stacked), and the new Message-ID is appended to any
    /// existing References chain.
    /// </summary>
    internal static (List<string> To, List<string>? Cc, string Subject, string? References) BuildReplyFields(
        IReadOnlyDictionary<string, string> headers, bool replyAll)
    {
        headers.TryGetValue("Subject", out var origSubject);
        headers.TryGetValue("From", out var origFrom);
        headers.TryGetValue("Reply-To", out var origReplyTo);
        headers.TryGetValue("To", out var origTo);
        headers.TryGetValue("Cc", out var origCc);
        headers.TryGetValue("Message-ID", out var origMessageId);
        headers.TryGetValue("References", out var origReferences);

        var replySubject = origSubject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
            ? origSubject
            : $"Re: {origSubject}";

        // Primary recipient: Reply-To wins, From is the fallback. Mailing
        // lists set Reply-To to route replies back to the list, not the sender.
        var primary = !string.IsNullOrWhiteSpace(origReplyTo) ? origReplyTo : origFrom;
        var to = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary))
            to.AddRange(SplitAddresses(primary));

        // --reply-all CCs all original recipients (original To + Cc), minus
        // anyone already in To (dedup is address-only so display-name
        // variations collapse).
        List<string>? cc = null;
        if (replyAll)
        {
            cc = [];
            if (!string.IsNullOrWhiteSpace(origTo)) cc.AddRange(SplitAddresses(origTo));
            if (!string.IsNullOrWhiteSpace(origCc)) cc.AddRange(SplitAddresses(origCc));

            var toAddresses = to.Select(ExtractEmailAddress).ToHashSet(StringComparer.OrdinalIgnoreCase);
            cc = cc.Where(x => !toAddresses.Contains(ExtractEmailAddress(x))).ToList();
            if (cc.Count == 0) cc = null;
        }

        // Append the original Message-ID to the References chain.
        var references = string.IsNullOrWhiteSpace(origReferences)
            ? origMessageId
            : $"{origReferences} {origMessageId}";

        return (to, cc, replySubject, references);
    }

    private static IEnumerable<string> SplitAddresses(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

    /// <summary>
    /// Parse a list of addresses, turning MimeKit's <see cref="MimeKit.ParseException"/>
    /// into a user-facing <see cref="NrException"/> (exit 2) that names the
    /// bad entry. Accepts both bare addresses and display-name forms
    /// ("Alice &lt;a@x.com&gt;"). MimeKit's parser is lenient (it will accept
    /// an unqualified token as a local-part); this adds a shape check so
    /// obvious non-addresses are rejected up front.
    /// </summary>
    internal static List<MailboxAddress> ParseAddressList(IEnumerable<string> addresses, string flag)
    {
        var result = new List<MailboxAddress>();
        foreach (var raw in addresses)
        {
            if (!MailboxAddress.TryParse(raw, out var parsed) || !LooksLikeEmailAddress(parsed.Address))
                throw new NrException(ExitCodes.InvalidArguments,
                    $"{flag}: '{raw}' is not a valid email address.");
            result.Add(parsed);
        }
        return result;
    }

    private static bool LooksLikeEmailAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        var at = address.IndexOf('@');
        if (at <= 0 || at >= address.Length - 1) return false;
        if (address.AsSpan(at + 1).IndexOf('.') < 0) return false;
        return !address.Any(char.IsWhiteSpace);
    }

    /// <summary>
    /// Returns the bare email address from an RFC 5322 mailbox form.
    /// "Alice &lt;a@x.com&gt;" → "a@x.com"; "a@x.com" → "a@x.com". No parsing
    /// of groups or quoted locals — this is address-level dedup, not display.
    /// </summary>
    internal static string ExtractEmailAddress(string mailbox)
    {
        var lt = mailbox.LastIndexOf('<');
        var gt = mailbox.LastIndexOf('>');
        if (lt >= 0 && gt > lt)
            return mailbox[(lt + 1)..gt].Trim();
        return mailbox.Trim();
    }

    // ── MIME builder ─────────────────────────────────────────────────────────

    // Gmail accepts up to 25 MB total message size on send. Base64 adds
    // roughly 1.37x; the on-disk threshold is set at 20 MB so the encoded
    // message stays comfortably under the API limit.
    internal const long GmailAttachmentLimitBytes = 20L * 1024 * 1024;

    private static async Task<Message> BuildRawMessageAsync(
        IList<string> to,
        IList<string>? cc,
        IList<string>? bcc,
        string subject,
        string body,
        IList<string>? attachmentPaths,
        string? inReplyTo = null,
        string? references = null)
    {
        var mime = new MimeMessage();
        mime.To.AddRange(ParseAddressList(to, "--to"));
        if (cc?.Count > 0) mime.Cc.AddRange(ParseAddressList(cc, "--cc"));
        if (bcc?.Count > 0) mime.Bcc.AddRange(ParseAddressList(bcc, "--bcc"));
        mime.Subject = subject;

        if (!string.IsNullOrEmpty(inReplyTo))
            mime.InReplyTo = inReplyTo;

        if (!string.IsNullOrEmpty(references))
            foreach (var r in references.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                mime.References.Add(r);

        var builder = new BodyBuilder { TextBody = body };

        if (attachmentPaths?.Count > 0)
        {
            long totalBytes = 0;
            foreach (var path in attachmentPaths)
            {
                if (!File.Exists(path))
                    throw new NrException(ExitCodes.FileError, $"Attachment not found: {path}");

                var size = new FileInfo(path).Length;
                totalBytes += size;
                // Gmail rejects anything over 25 MB total. Base64 adds ~37%,
                // so we warn earlier: 20 MB on disk ≈ 27 MB on the wire. The
                // actual API rejection is still the authoritative stop; this
                // just gives a precise exit 2 before the upload starts.
                if (totalBytes > GmailAttachmentLimitBytes)
                    throw new NrException(ExitCodes.InvalidArguments,
                        $"Attachments exceed Gmail's send limit (~{GmailAttachmentLimitBytes / (1024 * 1024)} MB combined). " +
                        "Share a link instead, or split the send.");

                await builder.Attachments.AddAsync(path);
            }
        }

        mime.Body = builder.ToMessageBody();

        using var ms = new MemoryStream();
        await mime.WriteToAsync(ms);
        var raw = Convert.ToBase64String(ms.ToArray())
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        return new Message { Raw = raw };
    }
}
