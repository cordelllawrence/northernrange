using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

/// <summary>
/// <c>nr messages …</c>: list, read, label, send, reply.
/// Send and reply live in <c>MessagesCommands.Send.cs</c>.
/// </summary>
public partial class MessagesCommands
{
    public static readonly string[] ListFormats = ["metadata", "minimal"];
    public static readonly string[] ReadFormats = ["full", "metadata", "minimal", "raw"];

    private readonly MessageService _messageService;
    private readonly LabelService _labelService;
    private readonly SendService _sendService;
    private readonly CommandPrelude _prelude;
    private readonly OutputWriter _output;
    private readonly ILogger<MessagesCommands> _logger;

    public MessagesCommands(
        MessageService messageService,
        LabelService labelService,
        SendService sendService,
        CommandPrelude prelude,
        OutputWriter output,
        ILogger<MessagesCommands> logger)
    {
        _messageService = messageService;
        _labelService = labelService;
        _sendService = sendService;
        _prelude = prelude;
        _output = output;
        _logger = logger;
    }

    [ErrorHandlingFilter]
    [Command("list", Description = "List messages with ID, From, Subject, Date, and snippet. See docs/USAGE.md for query syntax.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('l', Description = "Filter by label ID or name. Default: INBOX. Get user label IDs from 'nr labels list'.")] string? label = null,
        [Option('q', Description = "Gmail search query, same syntax as the Gmail search box.")] string? query = null,
        [Option('n', Description = "Max messages to return (1-500). Default: 25.")] int? max = null,
        [Option("page-token", Description = "Pagination token from a previous list response.")] string? pageToken = null,
        [Option("format", Description = "Detail level: 'metadata' (default) adds headers and snippet; 'minimal' returns IDs only.")] string format = "metadata")
    {
        using var session = _prelude.Begin(globals, _logger, "messages.list");

        var effectiveLabel = label ?? session.Config.DefaultLabel;
        var effectiveMax = max ?? session.Config.DefaultMaxResults;
        ParamValidation.RequireRange(effectiveMax, 1, 500, "max");
        format = ParamValidation.RequireOneOf(format, ListFormats, "format");

        var gmail = await session.GmailAsync();

        // Resolve label name to ID (e.g. "Inbox" → "INBOX")
        var resolvedLabel = (await _labelService.GetAsync(gmail, effectiveLabel)).Id;
        var result = await _messageService.ListAsync(gmail, resolvedLabel, query, effectiveMax, pageToken, format);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        // Gmail can return an empty page that still carries a token, so the
        // hint is printed whenever a token exists, not only after a table.
        if (result.Messages.Count == 0)
        {
            _output.WritePlain(result.NextPageToken is null ? "No messages found." : "No messages on this page.");
        }
        else
        {
            var headers = new[] { "ID", "From", "Subject", "Date", "Snippet" };
            var rows = result.Messages.Select(m => new[]
            {
                m.Id,
                PlainTextRenderer.Truncate(m.From, 30),
                PlainTextRenderer.Truncate(m.Subject, 40),
                PlainTextRenderer.FormatDate(m.Date, session.Config.DateFormat),
                PlainTextRenderer.Truncate(m.Snippet, 60)
            }).ToList();

            _output.WriteTable(headers, rows, session.Mode);
        }

        if (!string.IsNullOrEmpty(result.NextPageToken))
            _output.WritePlain(NextPageHint.Build("messages list", result.NextPageToken, globals,
                ("--label", label), ("--query", query), ("--max", max?.ToString()),
                ("--format", format == "metadata" ? null : format)));
    }

    [ErrorHandlingFilter]
    [Command("label", Description = "Add or remove labels on a message. Accepts label IDs or display names. Get message IDs from 'nr messages list'.")]
    public async Task LabelAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID. Get from 'nr messages list'.")] string id,
        [Option("add", Description = "Label ID or name to add. Repeat for multiple.")] List<string>? add = null,
        [Option("remove", Description = "Label ID or name to remove. Repeat for multiple.")] List<string>? remove = null)
    {
        if ((add is null or { Count: 0 }) && (remove is null or { Count: 0 }))
            throw new NrException(ExitCodes.InvalidArguments,
                "No label change given. Use --add <label> and/or --remove <label>, repeatable.");

        using var session = _prelude.Begin(globals, _logger, "messages.label",
            ("MessageId", id));

        var gmail = await session.GmailAsync();

        // Resolve label names to IDs
        var addIds = await ResolveLabelIdsAsync(gmail, add);
        var removeIds = await ResolveLabelIdsAsync(gmail, remove);

        var result = await _messageService.ModifyLabelsAsync(gmail, id, addIds, removeIds);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        var parts = new List<string>();
        if (addIds is { Count: > 0 })
            parts.Add($"added {string.Join(", ", add!)}");
        if (removeIds is { Count: > 0 })
            parts.Add($"removed {string.Join(", ", remove!)}");

        _output.WritePlain($"Updated message {id}: {string.Join("; ", parts)}.");
    }

    private async Task<List<string>?> ResolveLabelIdsAsync(Google.Apis.Gmail.v1.GmailService gmail, List<string>? labels)
    {
        if (labels is null or { Count: 0 })
            return null;

        var ids = new List<string>(labels.Count);
        foreach (var label in labels)
        {
            var resolved = await _labelService.GetAsync(gmail, label);
            ids.Add(resolved.Id);
        }
        return ids;
    }

    [ErrorHandlingFilter]
    [Command("read", Description = "Read one message: decoded body and attachment list. Get IDs from 'nr messages list'.")]
    public async Task ReadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID. Get from 'nr messages list'.")] string id,
        [Option("format", Description = "Detail level: 'full' (default) decoded body; 'metadata' headers only; 'minimal' IDs and labels only; 'raw' RFC 2822 bytes to stdout.")] string format = "full",
        [Option("include-headers", Description = "Header names to include with --format metadata. Comma-separated or repeated. Default: From,To,Cc,Subject,Date,Message-ID.")] List<string>? includeHeaders = null)
    {
        format = ParamValidation.RequireOneOf(format, ReadFormats, "format");
        var headerNames = ParamValidation.SplitList(includeHeaders);

        using var session = _prelude.Begin(globals, _logger, "messages.read",
            ("MessageId", id));

        var gmail = await session.GmailAsync();

        // Raw format (non-JSON): stream the original RFC 2822 bytes straight to
        // stdout — binary-safe, suitable for piping to a .eml file or another tool.
        if (format == "raw" && session.Mode != OutputMode.Json)
        {
            var rawBytes = await _messageService.GetRawAsync(gmail, id);
            await using var stdout = Console.OpenStandardOutput();
            await stdout.WriteAsync(rawBytes);
            return;
        }

        var message = await _messageService.GetAsync(gmail, id, format, headerNames);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(message);
            return;
        }

        // Human-readable output
        if (format == "minimal")
        {
            _output.WriteKeyValue([
                ("ID", message.Id),
                ("Thread", message.ThreadId ?? ""),
                ("Labels", string.Join(", ", message.LabelIds))
            ], session.Mode);
            return;
        }

        _output.WriteKeyValue([
            ("From", message.Headers.GetValueOrDefault("From", "")),
            ("To", message.Headers.GetValueOrDefault("To", "")),
            ("Subject", message.Headers.GetValueOrDefault("Subject", "")),
            ("Date", message.Headers.GetValueOrDefault("Date", ""))
        ], session.Mode);

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
}
