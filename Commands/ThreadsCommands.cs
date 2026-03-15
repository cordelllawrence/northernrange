using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

[ErrorHandlingFilter]
public class ThreadsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly ThreadService _threadService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<ThreadsCommands> _logger;

    public ThreadsCommands(
        GmailClientFactory gmailFactory,
        ThreadService threadService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<ThreadsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _threadService = threadService;
        _resolver = resolver;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List email threads. Supports --label, --query, --max, and --page-token.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('l', Description = "Filter by label ID or name (default: INBOX).")] string? label = null,
        [Option('q', Description = "Gmail search query — same syntax as the Gmail search box.")] string? query = null,
        [Option('n', Description = "Max threads to return (1–500). Default: 25.")] int? max = null,
        [Option("page-token", Description = "Pagination token from a previous list response.")] string? pageToken = null)
    {
        var ctx = _resolver.Resolve(globals);
        var effectiveLabel = label ?? ctx.Config.DefaultLabel;
        var effectiveMax = max ?? ctx.Config.DefaultMaxResults;
        ParamValidation.RequireRange(effectiveMax, 1, 500, "max");

        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "threads.list" });
        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var result = await _threadService.ListAsync(gmail, effectiveLabel, query, effectiveMax, pageToken);

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

    [Command("read", Description = "Read all messages in a thread in chronological order. Get IDs from 'nr threads list'.")]
    public async Task ReadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail thread ID. Get from 'nr threads list'.")] string id,
        [Option("format", Description = "'full' (default): body text. 'metadata': headers only. 'minimal': IDs only.")] string format = "full")
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "threads.read",
            ["ThreadId"] = id
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
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
