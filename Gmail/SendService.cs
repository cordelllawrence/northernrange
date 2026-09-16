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
                ["From", "To", "Cc", "Subject", "Message-ID", "References"]);
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
    /// original message's headers. The reply goes to the original sender; with
    /// <paramref name="replyAll"/>, the original To + Cc become the new Cc. The
    /// subject gains a single "Re: " prefix (not stacked), and the new
    /// Message-ID is appended to any existing References chain.
    /// </summary>
    internal static (List<string> To, List<string>? Cc, string Subject, string? References) BuildReplyFields(
        IReadOnlyDictionary<string, string> headers, bool replyAll)
    {
        headers.TryGetValue("Subject", out var origSubject);
        headers.TryGetValue("From", out var origFrom);
        headers.TryGetValue("To", out var origTo);
        headers.TryGetValue("Cc", out var origCc);
        headers.TryGetValue("Message-ID", out var origMessageId);
        headers.TryGetValue("References", out var origReferences);

        var replySubject = origSubject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
            ? origSubject
            : $"Re: {origSubject}";

        // Reply-to is always the original sender.
        var to = new List<string>();
        if (!string.IsNullOrWhiteSpace(origFrom)) to.Add(origFrom);

        // --reply-all CCs all original recipients (original To + Cc).
        List<string>? cc = null;
        if (replyAll)
        {
            cc = [];
            if (!string.IsNullOrWhiteSpace(origTo))
                cc.AddRange(origTo.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            if (!string.IsNullOrWhiteSpace(origCc))
                cc.AddRange(origCc.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        }

        // Append the original Message-ID to the References chain.
        var references = string.IsNullOrWhiteSpace(origReferences)
            ? origMessageId
            : $"{origReferences} {origMessageId}";

        return (to, cc, replySubject, references);
    }

    // ── MIME builder ─────────────────────────────────────────────────────────

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
        mime.To.AddRange(to.Select(MailboxAddress.Parse));
        if (cc?.Count  > 0) mime.Cc.AddRange(cc.Select(MailboxAddress.Parse));
        if (bcc?.Count > 0) mime.Bcc.AddRange(bcc.Select(MailboxAddress.Parse));
        mime.Subject = subject;

        if (!string.IsNullOrEmpty(inReplyTo))
            mime.InReplyTo = inReplyTo;

        if (!string.IsNullOrEmpty(references))
            foreach (var r in references.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                mime.References.Add(r);

        var builder = new BodyBuilder { TextBody = body };

        if (attachmentPaths?.Count > 0)
        {
            foreach (var path in attachmentPaths)
            {
                if (!File.Exists(path))
                    throw new NrException(ExitCodes.FileError, $"Attachment not found: {path}");
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
