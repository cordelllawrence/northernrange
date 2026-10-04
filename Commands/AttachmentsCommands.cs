using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class AttachmentsCommands
{
    private readonly AttachmentService _attachmentService;
    private readonly CommandPrelude _prelude;
    private readonly OutputWriter _output;
    private readonly ILogger<AttachmentsCommands> _logger;

    public AttachmentsCommands(
        AttachmentService attachmentService,
        CommandPrelude prelude,
        OutputWriter output,
        ILogger<AttachmentsCommands> logger)
    {
        _attachmentService = attachmentService;
        _prelude = prelude;
        _output = output;
        _logger = logger;
    }

    [ErrorHandlingFilter]
    [Command("list", Description = "List a message's attachments without downloading them. Use --json to get attachment IDs.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID. Find messages with 'nr messages list -q \"has:attachment\"'.")] string messageId)
    {
        using var session = _prelude.Begin(globals, _logger, "attachments.list",
            ("MessageId", messageId));

        var gmail = await session.GmailAsync();
        var result = await _attachmentService.ListFromMessageAsync(gmail, messageId);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        if (result.Attachments.Count == 0)
        {
            _output.WritePlain($"No attachments found in message {messageId}.");
            return;
        }

        _output.WritePlain($"Attachments for message {messageId}:");
        _output.WritePlain("");

        var headers = new[] { "Index", "Filename", "MIME Type", "Size" };
        var rows = result.Attachments.Select((a, i) => new[]
        {
            (i + 1).ToString(),
            a.Filename,
            a.MimeType,
            PlainTextRenderer.FormatSize(a.Size)
        }).ToList();

        _output.WriteTable(headers, rows, session.Mode);
    }

    [ErrorHandlingFilter]
    [Command("download", Description = "Download one attachment to disk. Get attachment IDs from 'nr attachments list <message-id> --json'.")]
    public async Task DownloadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID containing the attachment.")] string messageId,
        [Argument(Description = "Attachment ID from 'nr attachments list <id> --json' (the 'attachmentId' field).")] string attachmentId,
        [Option('o', Description = "Destination file or directory. Default: current directory using original filename.")] string? output = null,
        [Option("force", Description = "Overwrite the output file if it already exists.")] bool force = false)
    {
        using var session = _prelude.Begin(globals, _logger, "attachments.download",
            ("MessageId", messageId),
            ("AttachmentId", attachmentId));

        var gmail = await session.GmailAsync();
        var result = await _attachmentService.DownloadAsync(gmail, messageId, attachmentId, output, force);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        _output.WritePlain(
            $"Downloaded {result.Filename} ({PlainTextRenderer.FormatSize(result.Size)}) to {result.OutputPath}.");
    }
}
