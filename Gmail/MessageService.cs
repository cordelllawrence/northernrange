using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Util;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Mime;
using NorthernRange.Models;

namespace NorthernRange.Gmail;

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
            throw MapApiException(ex);
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

        // Fetch metadata for each message in parallel
        var tasks = listResp.Messages.Select(m => FetchSummaryAsync(gmail, m.Id, ct));
        var summaries = await Task.WhenAll(tasks);

        return new MessageListResult(
            summaries.ToList(),
            listResp.NextPageToken,
            listResp.ResultSizeEstimate ?? 0);
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
            throw MapApiException(ex);
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
        else if (format != "metadata")
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
            throw MapApiException(ex);
        }

        return new ModifyMessageResult(msg.Id, msg.LabelIds?.ToList() ?? []);
    }

    private static NrException MapApiException(Google.GoogleApiException ex) =>
        ex.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.NotFound =>
                new NrException(ExitCodes.NotFound, $"Resource not found: {ex.Error?.Message ?? ex.Message}"),
            System.Net.HttpStatusCode.Unauthorized =>
                new NrException(ExitCodes.AuthRequired, "Authentication expired. Run 'nr auth login'."),
            System.Net.HttpStatusCode.Forbidden =>
                new NrException(ExitCodes.ApiError, $"Access denied: {ex.Error?.Message ?? ex.Message}"),
            _ =>
                new NrException(ExitCodes.ApiError, $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}")
        };
}
