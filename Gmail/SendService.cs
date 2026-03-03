using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Microsoft.Extensions.Logging;
using MimeKit;
using NorthernRange.Errors;
using NorthernRange.Models;
using NrMimeParser = NorthernRange.Mime.MimeParser;

namespace NorthernRange.Gmail;

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
            throw MapApiException(ex);
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
            throw MapApiException(ex);
        }

        var headers = NrMimeParser.ParseHeaders(original.Payload?.Headers);
        headers.TryGetValue("Subject",    out var origSubject);
        headers.TryGetValue("From",       out var origFrom);
        headers.TryGetValue("To",         out var origTo);
        headers.TryGetValue("Cc",         out var origCc);
        headers.TryGetValue("Message-ID", out var origMessageId);
        headers.TryGetValue("References", out var origReferences);

        var replySubject = origSubject?.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) == true
            ? origSubject
            : $"Re: {origSubject}";

        // Reply-to is always the original sender
        var to = new List<string>();
        if (!string.IsNullOrWhiteSpace(origFrom)) to.Add(origFrom);

        // --reply-all CCs all original recipients
        List<string>? cc = null;
        if (replyAll)
        {
            cc = [];
            if (!string.IsNullOrWhiteSpace(origTo))
                cc.AddRange(origTo.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            if (!string.IsNullOrWhiteSpace(origCc))
                cc.AddRange(origCc.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        }

        // Build References chain
        var references = string.IsNullOrWhiteSpace(origReferences)
            ? origMessageId
            : $"{origReferences} {origMessageId}";

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
            throw MapApiException(ex);
        }
    }

    // ── Draft management ─────────────────────────────────────────────────────

    public async Task<DraftListResult> ListDraftsAsync(
        GmailService gmail,
        int maxResults,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ListDrafts. Max={Max}", maxResults);

        ListDraftsResponse listResp;
        try
        {
            var listReq = gmail.Users.Drafts.List("me");
            listReq.MaxResults = maxResults;
            listResp = await listReq.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw MapApiException(ex);
        }

        if (listResp.Drafts is null || listResp.Drafts.Count == 0)
            return new DraftListResult([], listResp.NextPageToken, listResp.ResultSizeEstimate ?? 0);

        // Fetch metadata for each draft in parallel
        var summaries = await Task.WhenAll(listResp.Drafts.Select(d =>
            FetchDraftSummaryAsync(gmail, d.Id, ct)));

        return new DraftListResult(
            [.. summaries.OrderByDescending(s => s.Date ?? DateTimeOffset.MinValue)],
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
            throw MapApiException(ex);
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
            throw MapApiException(ex);
        }
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

    // ── Error mapping ─────────────────────────────────────────────────────────

    private static NrException MapApiException(Google.GoogleApiException ex) =>
        ex.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized =>
                new NrException(ExitCodes.AuthRequired, "Authentication expired. Run 'nr auth login'."),
            System.Net.HttpStatusCode.Forbidden =>
                new NrException(ExitCodes.ApiError,
                    "Insufficient permissions — run 'nr auth login' to grant send/modify access."),
            System.Net.HttpStatusCode.NotFound =>
                new NrException(ExitCodes.NotFound, $"Resource not found: {ex.Error?.Message ?? ex.Message}"),
            _ =>
                new NrException(ExitCodes.ApiError,
                    $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}")
        };
}
