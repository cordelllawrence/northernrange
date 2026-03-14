using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class MessagesCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly MessageService _messageService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<MessagesCommands> _logger;

    public MessagesCommands(
        GmailClientFactory gmailFactory,
        MessageService messageService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<MessagesCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _messageService = messageService;
        _resolver = resolver;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List messages. Returns ID, From, Subject, Date, and snippet. See USAGE.md for query syntax and examples.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('l', Description = "Filter by label ID or name (default: INBOX). Get user label IDs from 'nr labels list'.")] string? label = null,
        [Option('q', Description = "Gmail search query — same syntax as the Gmail search box.")] string? query = null,
        [Option('n', Description = "Max messages to return (1–500). Default: 25.")] int? max = null,
        [Option("page-token", Description = "Pagination token from a previous list response.")] string? pageToken = null,
        [Option("format", Description = "'metadata' (default): headers + snippet. 'minimal': IDs only.")] string format = "metadata")
    {
        var ctx = _resolver.Resolve(globals);
        var effectiveLabel = label ?? ctx.Config.DefaultLabel;
        var effectiveMax = max ?? ctx.Config.DefaultMaxResults;

        if (effectiveMax is < 1 or > 500)
        {
            _output.WriteError($"--max must be between 1 and 500 (got {effectiveMax}). Run 'nr messages list --help' for usage.");
            Environment.Exit(ExitCodes.InvalidArguments);
            return;
        }

        var mode = _output.DetermineMode(globals, ctx.Config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "messages.list" });
            var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
            var result = await _messageService.ListAsync(gmail, effectiveLabel, query, effectiveMax, pageToken, format);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(result);
                return;
            }

            if (result.Messages.Count == 0)
            {
                _output.WritePlain("No messages found.");
                return;
            }

            var headers = new[] { "ID", "From", "Subject", "Date", "Snippet" };
            var rows = result.Messages.Select(m => new[]
            {
                m.Id,
                PlainTextRenderer.Truncate(m.From, 30),
                PlainTextRenderer.Truncate(m.Subject, 40),
                PlainTextRenderer.FormatDate(m.Date, ctx.Config.DateFormat),
                PlainTextRenderer.Truncate(m.Snippet, 60)
            }).ToList();

            _output.WriteTable(headers, rows, mode);

            if (!string.IsNullOrEmpty(result.NextPageToken))
                _output.WritePlain($"Next page: nr messages list --page-token {result.NextPageToken}");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "messages list failed");
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("read", Description = "Read a single message by ID. Decodes body and lists attachments. Get IDs from 'nr messages list'.")]
    public async Task ReadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID. Get from 'nr messages list'.")] string id,
        [Option("format", Description = "'full' (default): decoded body. 'metadata': headers only. 'raw': RFC 2822 bytes to stdout.")] string format = "full",
        [Option("include-headers", Description = "Comma-separated headers to include with --format metadata. Default: From,To,Cc,Subject,Date,Message-ID.")] string? includeHeaders = null)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        var headerNames = string.IsNullOrEmpty(includeHeaders)
            ? null
            : includeHeaders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "messages.read",
                ["MessageId"] = id
            });

            var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
            var message = await _messageService.GetAsync(gmail, id, format, headerNames);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(message);
                return;
            }

            // For raw format, output directly to stdout
            if (format == "raw" && message.Body?.Text is not null)
            {
                Console.Write(message.Body.Text);
                return;
            }

            // Human-readable output
            _output.WriteKeyValue([
                ("From", message.Headers.GetValueOrDefault("From", "")),
                ("To", message.Headers.GetValueOrDefault("To", "")),
                ("Subject", message.Headers.GetValueOrDefault("Subject", "")),
                ("Date", message.Headers.GetValueOrDefault("Date", ""))
            ], mode);

            if (!string.IsNullOrEmpty(message.Snippet) && format == "metadata")
            {
                _output.WritePlain("");
                _output.WritePlain(message.Snippet);
            }

            if (message.Body?.Text is not null)
            {
                _output.WritePlain("");
                _output.WritePlain(message.Body.Text);
            }

            if (message.Attachments.Count > 0)
            {
                _output.WritePlain("");
                _output.WriteDivider("Attachments");
                for (var i = 0; i < message.Attachments.Count; i++)
                {
                    var att = message.Attachments[i];
                    _output.WritePlain(
                        $"[{i + 1}] {att.Filename} ({att.MimeType}, {PlainTextRenderer.FormatSize(att.Size)}) — attachment-id: {att.AttachmentId}");
                }
            }
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "messages read failed for {MessageId}", id);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }
}
