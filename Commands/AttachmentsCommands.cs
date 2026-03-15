using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

[ErrorHandlingFilter]
public class AttachmentsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly AttachmentService _attachmentService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<AttachmentsCommands> _logger;

    public AttachmentsCommands(
        GmailClientFactory gmailFactory,
        AttachmentService attachmentService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<AttachmentsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _attachmentService = attachmentService;
        _resolver = resolver;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List attachments in a message without downloading. Use --json to get attachment IDs.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID. Find messages with 'nr messages list -q \"has:attachment\"'.")] string messageId)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "attachments.list",
            ["MessageId"] = messageId
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var result = await _attachmentService.ListFromMessageAsync(gmail, messageId);

        if (mode == OutputMode.Json)
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

        _output.WriteTable(headers, rows, mode);
    }

    [Command("download", Description = "Download an attachment to disk. Get attachment IDs from 'nr attachments list <id> --json'.")]
    public async Task DownloadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID containing the attachment.")] string messageId,
        [Argument(Description = "Attachment ID from 'nr attachments list <id> --json' (the 'attachmentId' field).")] string attachmentId,
        [Option('o', Description = "Destination file or directory. Default: current directory using original filename.")] string? output = null,
        [Option("force", Description = "Overwrite the output file if it already exists.")] bool force = false)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "attachments.download",
            ["MessageId"] = messageId,
            ["AttachmentId"] = attachmentId
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var result = await _attachmentService.DownloadAsync(gmail, messageId, attachmentId, output, force);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        _output.WritePlain(
            $"Downloaded {result.Filename} ({PlainTextRenderer.FormatSize(result.Size)}) to {result.OutputPath}");
    }
}
