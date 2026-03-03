using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Mime;
using NorthernRange.Models;

namespace NorthernRange.Gmail;

public class AttachmentService
{
    private readonly MimeParser _mimeParser;
    private readonly ILogger<AttachmentService> _logger;

    public AttachmentService(MimeParser mimeParser, ILogger<AttachmentService> logger)
    {
        _mimeParser = mimeParser;
        _logger = logger;
    }

    public async Task<AttachmentListResult> ListFromMessageAsync(
        GmailService gmail,
        string messageId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Listing attachments for message {MessageId}", messageId);

        Google.Apis.Gmail.v1.Data.Message msg;
        try
        {
            var req = gmail.Users.Messages.Get("me", messageId);
            req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
            msg = await req.ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NrException(ExitCodes.NotFound, $"Message '{messageId}' not found.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw new NrException(ExitCodes.ApiError,
                $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}");
        }

        var attachments = _mimeParser.ExtractAttachments(msg.Payload);
        return new AttachmentListResult(messageId, attachments);
    }

    public async Task<AttachmentDownloadResult> DownloadAsync(
        GmailService gmail,
        string messageId,
        string attachmentId,
        string? outputPath,
        bool force,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Downloading attachment {AttachmentId} from message {MessageId}",
            attachmentId, messageId);

        // Resolve the filename from the message payload
        var (filename, mimeType) = await ResolveAttachmentMetaAsync(gmail, messageId, attachmentId, ct);

        // Resolve output path
        var finalPath = ResolveOutputPath(outputPath, filename);

        if (File.Exists(finalPath) && !force)
            throw new NrException(ExitCodes.FileError,
                $"File already exists: {finalPath}. Use --force to overwrite.");

        // Fetch attachment data
        Google.Apis.Gmail.v1.Data.MessagePartBody body;
        try
        {
            body = await gmail.Users.Messages.Attachments
                .Get("me", messageId, attachmentId)
                .ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new NrException(ExitCodes.NotFound,
                $"Attachment '{attachmentId}' not found in message '{messageId}'.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw new NrException(ExitCodes.ApiError,
                $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}");
        }

        if (string.IsNullOrEmpty(body.Data))
            throw new NrException(ExitCodes.ApiError, "Attachment data is empty.");

        var bytes = MimeParser.DecodeBase64Url(body.Data);

        try
        {
            var dir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllBytesAsync(finalPath, bytes, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new NrException(ExitCodes.FileError,
                $"Could not write to {finalPath}: {ex.Message}", ex);
        }

        _logger.LogInformation("Attachment saved to {OutputPath}", finalPath);

        return new AttachmentDownloadResult(
            messageId,
            attachmentId,
            filename,
            mimeType,
            bytes.LongLength,
            finalPath);
    }

    private async Task<(string Filename, string MimeType)> ResolveAttachmentMetaAsync(
        GmailService gmail,
        string messageId,
        string attachmentId,
        CancellationToken ct)
    {
        var req = gmail.Users.Messages.Get("me", messageId);
        req.Format = UsersResource.MessagesResource.GetRequest.FormatEnum.Full;
        var msg = await req.ExecuteAsync(ct);

        var attachments = _mimeParser.ExtractAttachments(msg.Payload);
        var match = attachments.FirstOrDefault(a => a.AttachmentId == attachmentId);
        if (match is null)
            return ($"attachment-{attachmentId[..Math.Min(8, attachmentId.Length)]}", "application/octet-stream");

        return (match.Filename, match.MimeType);
    }

    private static string ResolveOutputPath(string? outputPath, string filename)
    {
        if (string.IsNullOrEmpty(outputPath))
            return Path.Combine(Directory.GetCurrentDirectory(), filename);

        if (Directory.Exists(outputPath))
            return Path.Combine(outputPath, filename);

        return outputPath;
    }
}
