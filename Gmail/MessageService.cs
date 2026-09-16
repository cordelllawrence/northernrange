using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Util;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Mime;
using NorthernRange.Models;

namespace NorthernRange.Gmail;

/// <summary>
/// Gmail <c>users.messages</c> operations: list, read (metadata/full/raw),
/// and label modification. Maps Google API types to <see cref="Models"/> records
/// and translates <see cref="Google.GoogleApiException"/> into <see cref="NrException"/>.
/// </summary>
public class MessageService
{
    private static readonly string[] DefaultMetadataHeaders = ["From", "To", "Subject", "Date", "Cc", "Message-ID"];

    private readonly MimeParser _mimeParser;
    private readonly ILogger<MessageService> _logger;

    public MessageService(MimeParser mimeParser, ILogger<MessageService> logger)
    {
        _mimeParser = mimeParser;
        _logger = logger;
    }

    public async Task<MessageListResult> ListAsync(
        GmailService gmail,
        string labelId,
        string? query,
        int maxResults,
        string? pageToken,
        string format,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Listing messages. Label={LabelId}, Query={Query}, Max={Max}", labelId, query, maxResults);

        var listReq = gmail.Users.Messages.List("me");
        listReq.LabelIds = new Repeatable<string>([labelId]);
        listReq.Q = query;
        listReq.MaxResults = maxResults;
        listReq.PageToken = pageToken;

        ListMessagesResponse listResp;
        try
        {
            listResp = await listReq.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }

        if (listResp.Messages is null || listResp.Messages.Count == 0)
            return new MessageListResult([], listResp.NextPageToken, listResp.ResultSizeEstimate ?? 0);

        if (format == "minimal")
        {
            var minimal = listResp.Messages
                .Select(m => new MessageSummary(m.Id, m.ThreadId, null, null, null, null, null, m.LabelIds?.ToList() ?? []))
                .ToList();
            return new MessageListResult(minimal, listResp.NextPageToken, listResp.ResultSizeEstimate ?? 0);
        }

        // Fetch metadata for each message with bounded concurrency. The list API
        // returns only IDs, so a per-message get is unavoidable; capping parallelism
        // keeps large --max values from spiking requests and triggering 429s.
        var summaries = await FetchSummariesAsync(gmail, listResp.Messages, ct);

        return new MessageListResult(
            summaries,
            listResp.NextPageToken,
            listResp.ResultSizeEstimate ?? 0);
    }

    private const int MaxConcurrentFetches = 10;

    private async Task<List<MessageSummary>> FetchSummariesAsync(
        GmailService gmail, IList<Message> messages, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxConcurrentFetches);
        var tasks = messages.Select(async m =>
        {
            await gate.WaitAsync(ct);
            try { return await FetchSummaryAsync(gmail, m.Id, ct); }
            finally { gate.Release(); }
        });
        // Task.WhenAll preserves input order in its result array.
        return [.. await Task.WhenAll(tasks)];
    }

    private async Task<MessageSummary> FetchSummaryAsync(GmailService gmail, string id, CancellationToken ct)
    {
        var req = gmail.Users.Messages.Get("me", id);
        req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
        req.MetadataHeaders = new Repeatable<string>(["From", "To", "Subject", "Date"]);

        var msg = await req.ExecuteAsync(ct);
        var headers = MimeParser.ParseHeaders(msg.Payload?.Headers);

        DateTimeOffset? date = null;
        if (headers.TryGetValue("Date", out var dateStr) && !string.IsNullOrEmpty(dateStr))
            if (MimeKit.Utils.DateUtils.TryParse(dateStr, out var parsed)) date = parsed;

        return new MessageSummary(
            msg.Id,
            msg.ThreadId,
            headers.GetValueOrDefault("From"),
            headers.GetValueOrDefault("To"),
            headers.GetValueOrDefault("Subject"),
            date,
            msg.Snippet,
            msg.LabelIds?.ToList() ?? []);
    }

    public async Task<MessageDetail> GetAsync(
        GmailService gmail,
        string id,
        string format,
        IEnumerable<string>? includeHeaders,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Getting message {MessageId} format={Format}", id, format);

        var req = gmail.Users.Messages.Get("me", id);

        if (format == "raw")
        {
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;
        }
        else if (format == "minimal")
        {
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Minimal;
        }
        else if (format == "metadata")
        {
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Metadata;
            var headerNames = includeHeaders?.ToArray() ?? DefaultMetadataHeaders;
            req.MetadataHeaders = new Repeatable<string>(headerNames);
        }
        else
        {
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        }

        Message msg;
        try
        {
            msg = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NrException(ExitCodes.NotFound, $"Message '{id}' not found.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }

        var allHeaders = MimeParser.ParseHeaders(msg.Payload?.Headers);
        var filteredHeaders = format == "metadata" && includeHeaders != null
            ? includeHeaders
                .Select(h => (Key: h, Value: allHeaders.TryGetValue(h, out var v) ? v : ""))
                .ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
            : allHeaders.ToDictionary(kv => kv.Key, kv => kv.Value);

        MessageBody? body = null;
        var attachments = new List<AttachmentInfo>();

        if (format == "raw" && msg.Raw is not null)
        {
            var rawBytes = MimeParser.DecodeBase64Url(msg.Raw);
            body = new MessageBody("message/rfc822", System.Text.Encoding.UTF8.GetString(rawBytes));
        }
        else if (format == "full")
        {
            body = _mimeParser.ParseBody(msg.Payload);
            attachments = _mimeParser.ExtractAttachments(msg.Payload);
        }

        return new MessageDetail(
            msg.Id,
            msg.ThreadId,
            msg.LabelIds?.ToList() ?? [],
            filteredHeaders,
            msg.Snippet,
            body,
            attachments,
            MimeParser.ParseInternalDate(msg.InternalDate),
            msg.SizeEstimate ?? 0);
    }

    /// <summary>
    /// Fetches a message in <c>raw</c> format and returns the original RFC 2822
    /// bytes (base64url-decoded). Used for binary-safe streaming to stdout — the
    /// bytes must not be round-tripped through a string, which would corrupt any
    /// non-UTF8 content.
    /// </summary>
    public async Task<byte[]> GetRawAsync(GmailService gmail, string id, CancellationToken ct = default)
    {
        var req = gmail.Users.Messages.Get("me", id);
        req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Raw;

        Message msg;
        try
        {
            msg = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex, $"Message '{id}' not found.");
        }

        if (string.IsNullOrEmpty(msg.Raw))
            throw new NrException(ExitCodes.ApiError, "Message has no raw content.");

        return MimeParser.DecodeBase64Url(msg.Raw);
    }

    public async Task<ModifyMessageResult> ModifyLabelsAsync(
        GmailService gmail,
        string messageId,
        IList<string>? addLabelIds,
        IList<string>? removeLabelIds,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Modifying labels on message {MessageId}. Add={Add}, Remove={Remove}",
            messageId, addLabelIds, removeLabelIds);

        var body = new Google.Apis.Gmail.v1.Data.ModifyMessageRequest
        {
            AddLabelIds = addLabelIds,
            RemoveLabelIds = removeLabelIds
        };

        Google.Apis.Gmail.v1.Data.Message msg;
        try
        {
            msg = await gmail.Users.Messages.Modify(body, "me", messageId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NrException(ExitCodes.NotFound, $"Message '{messageId}' not found.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw GmailErrorMapper.Map(ex);
        }

        return new ModifyMessageResult(msg.Id, msg.LabelIds?.ToList() ?? []);
    }
}
