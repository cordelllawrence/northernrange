using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class ThreadsCommands
{
    public static readonly string[] ReadFormats = ["full", "metadata", "minimal"];

    private readonly GmailClientFactory _gmailFactory;
    private readonly ThreadService _threadService;
    private readonly LabelService _labelService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<ThreadsCommands> _logger;

    public ThreadsCommands(
        GmailClientFactory gmailFactory,
        ThreadService threadService,
        LabelService labelService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<ThreadsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _threadService = threadService;
        _labelService = labelService;
        _resolver = resolver;
        _output = output;
        _logger = logger;
    }

    [ErrorHandlingFilter]
    [Command("list", Description = "List threads with ID, message count, and snippet. Same filters as 'nr messages list'.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('l', Description = "Filter by label ID or name. Default: INBOX. Get user label IDs from 'nr labels list'.")] string? label = null,
        [Option('q', Description = "Gmail search query, same syntax as the Gmail search box.")] string? query = null,
        [Option('n', Description = "Max threads to return (1-500). Default: 25.")] int? max = null,
        [Option("page-token", Description = "Pagination token from a previous list response.")] string? pageToken = null)
    {
        var ctx = _resolver.Resolve(globals);
        var effectiveLabel = label ?? ctx.Config.DefaultLabel;
        var effectiveMax = max ?? ctx.Config.DefaultMaxResults;
        ParamValidation.RequireRange(effectiveMax, 1, 500, "max");

        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "threads.list" });
        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath, ctx.Config.HttpTimeoutSeconds);

        // Resolve label name to ID (e.g. "Work/Projects" → "Label_18"), matching `messages list`
        var resolvedLabel = (await _labelService.GetAsync(gmail, effectiveLabel)).Id;
        var result = await _threadService.ListAsync(gmail, resolvedLabel, query, effectiveMax, pageToken);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        if (result.Threads.Count == 0)
        {
            _output.WritePlain("No threads found.");
            return;
        }

        var headers = new[] { "ID", "Messages", "Snippet" };
        var rows = result.Threads.Select(t => new[]
        {
            t.Id,
            t.MessageCount?.ToString() ?? "-",
            PlainTextRenderer.Truncate(t.Snippet, 80)
        }).ToList();

        _output.WriteTable(headers, rows, mode);

        if (!string.IsNullOrEmpty(result.NextPageToken))
            _output.WritePlain($"Next page: nr threads list --page-token {result.NextPageToken}");
    }

    [ErrorHandlingFilter]
    [Command("read", Description = "Read every message in a thread, oldest first. Get IDs from 'nr threads list'.")]
    public async Task ReadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail thread ID. Get from 'nr threads list'.")] string id,
        [Option("format", Description = "Detail level: 'full' (default) body text; 'metadata' headers only; 'minimal' IDs only.")] string format = "full")
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);
        format = ParamValidation.RequireOneOf(format, ReadFormats, "format");

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "threads.read",
            ["ThreadId"] = id
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath, ctx.Config.HttpTimeoutSeconds);
        var thread = await _threadService.GetAsync(gmail, id, format);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(thread);
            return;
        }

        _output.WritePlain($"Thread: {thread.Id}  ({thread.Messages.Count} messages)");
        _output.WritePlain("");

        foreach (var msg in thread.Messages)
        {
            _output.WriteDivider($"Message {msg.Id}");
            _output.WriteKeyValue([
                ("From", msg.Headers.GetValueOrDefault("From", "")),
                ("Date", msg.Headers.GetValueOrDefault("Date", ""))
            ], mode);

            if (msg.Body?.Text is not null)
            {
                _output.WritePlain("");
                _output.WritePlain(msg.Body.Text);
            }
            _output.WritePlain("");
        }
    }
}
