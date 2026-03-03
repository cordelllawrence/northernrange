using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class AttachmentsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly AttachmentService _attachmentService;
    private readonly ConfigLoader _configLoader;
    private readonly OutputWriter _output;
    private readonly ILogger<AttachmentsCommands> _logger;

    public AttachmentsCommands(
        GmailClientFactory gmailFactory,
        AttachmentService attachmentService,
        ConfigLoader configLoader,
        OutputWriter output,
        ILogger<AttachmentsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _attachmentService = attachmentService;
        _configLoader = configLoader;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List all attachments in a message without downloading them. " +
        "Shows filename, MIME type, and size for each attachment. " +
        "Use --json to get the attachment IDs needed for the 'nr attachments download' command. " +
        "Find messages with attachments using: 'nr messages list -q \"has:attachment\"'. " +
        "Examples: 'nr attachments list 19c8fe3345e9052c' | 'nr attachments list <message-id> --json'")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID containing the attachments. " +
            "Find messages with attachments using: 'nr messages list -q \"has:attachment\"'. " +
            "The message ID appears in 'nr messages list' output.")] string messageId)
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "attachments.list",
                ["MessageId"] = messageId
            });

            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
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
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "attachments list failed for {MessageId}", messageId);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("download", Description = "Download an attachment from a message to disk. " +
        "Get the message ID from 'nr messages list' and the attachment ID from 'nr attachments list <message-id> --json'. " +
        "Without --output the file is written to the current directory using the original filename. " +
        "Without --force the command exits with code 6 if the file already exists. " +
        "Examples: 'nr attachments download <msg-id> <att-id>' | " +
        "'nr attachments download <msg-id> <att-id> --output ~/Downloads' | " +
        "'nr attachments download <msg-id> <att-id> --output ~/docs/report.pdf --force'")]
    public async Task DownloadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID that contains the attachment. " +
            "Use 'nr messages list -q \"has:attachment\"' to find messages with attachments.")] string messageId,
        [Argument(Description = "Attachment ID from 'nr attachments list <message-id> --json' (the 'attachmentId' field). " +
            "Looks like: ANGjdJ_PiF0Gga...")] string attachmentId,
        [Option('o', Description = "Destination file or directory path. " +
            "If a directory path is given, the original attachment filename is used inside that directory. " +
            "If omitted, the file is written to the current working directory using the original filename. " +
            "Examples: -o ~/Downloads | -o ~/docs/report.pdf")] string? output = null,
        [Option("force", Description = "Overwrite the output file if it already exists. " +
            "Without this flag the command exits with code 6 when the destination file is already present. " +
            "Example: --force")] bool force = false)
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "attachments.download",
                ["MessageId"] = messageId,
                ["AttachmentId"] = attachmentId
            });

            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
            var result = await _attachmentService.DownloadAsync(gmail, messageId, attachmentId, output, force);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(result);
                return;
            }

            _output.WritePlain(
                $"Downloaded {result.Filename} ({PlainTextRenderer.FormatSize(result.Size)}) to {result.OutputPath}");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "attachments download failed for {MessageId}/{AttachmentId}", messageId, attachmentId);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }
}
