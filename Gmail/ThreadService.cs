using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Google.Apis.Util;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Mime;
using NorthernRange.Models;

namespace NorthernRange.Gmail;

public class ThreadService
{
    private readonly MimeParser _mimeParser;
    private readonly ILogger<ThreadService> _logger;

    public ThreadService(MimeParser mimeParser, ILogger<ThreadService> logger)
    {
        _mimeParser = mimeParser;
        _logger = logger;
    }

    public async Task<ThreadListResult> ListAsync(
        GmailService gmail,
        string labelId,
        string? query,
        int maxResults,
        string? pageToken,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Listing threads. Label={LabelId}, Max={Max}", labelId, maxResults);

        var req = gmail.Users.Threads.List("me");
        req.LabelIds = new Repeatable<string>([labelId]);
        req.Q = query;
        req.MaxResults = maxResults;
        req.PageToken = pageToken;

        ListThreadsResponse resp;
        try
        {
            resp = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw MapApiException(ex);
        }

        if (resp.Threads is null || resp.Threads.Count == 0)
            return new ThreadListResult([], resp.NextPageToken, resp.ResultSizeEstimate ?? 0);

        var summaries = resp.Threads
            .Select(t => new ThreadSummary(
                t.Id,
                t.Snippet,
                t.Messages?.Count,
                t.HistoryId?.ToString()))
            .ToList();

        return new ThreadListResult(summaries, resp.NextPageToken, resp.ResultSizeEstimate ?? 0);
    }

    public async Task<ThreadDetail> GetAsync(
        GmailService gmail,
        string id,
        string format,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Getting thread {ThreadId} format={Format}", id, format);

        var req = gmail.Users.Threads.Get("me", id);
        req.Format = format switch
        {
            "minimal" => UsersResource.ThreadsResource.GetRequest.FormatEnum.Minimal,
            "metadata" => UsersResource.ThreadsResource.GetRequest.FormatEnum.Metadata,
            _ => UsersResource.ThreadsResource.GetRequest.FormatEnum.Full
        };

        Google.Apis.Gmail.v1.Data.Thread thread;
        try
        {
            thread = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NrException(ExitCodes.NotFound, $"Thread '{id}' not found.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw MapApiException(ex);
        }

        var messages = (thread.Messages ?? [])
            .OrderBy(m => m.InternalDate ?? 0)
            .Select(m => MapMessage(m, format))
            .ToList();

        return new ThreadDetail(
            thread.Id,
            thread.HistoryId?.ToString(),
            messages);
    }

    private MessageDetail MapMessage(Message msg, string format)
    {
        var headers = MimeParser.ParseHeaders(msg.Payload?.Headers);
        MessageBody? body = null;
        var attachments = new List<AttachmentInfo>();

        if (format == "full")
        {
            body = _mimeParser.ParseBody(msg.Payload);
            attachments = _mimeParser.ExtractAttachments(msg.Payload);
        }

        return new MessageDetail(
            msg.Id,
            msg.ThreadId,
            msg.LabelIds?.ToList() ?? [],
            headers,
            msg.Snippet,
            body,
            attachments,
            MimeParser.ParseInternalDate(msg.InternalDate),
            msg.SizeEstimate ?? 0);
    }

    private static NrException MapApiException(Google.GoogleApiException ex) =>
        ex.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.NotFound =>
                new NrException(ExitCodes.NotFound, $"Resource not found: {ex.Error?.Message ?? ex.Message}"),
            System.Net.HttpStatusCode.Unauthorized =>
                new NrException(ExitCodes.AuthRequired, "Authentication expired. Run 'nr auth login'."),
            _ =>
                new NrException(ExitCodes.ApiError, $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}")
        };
}
