using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class ThreadsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly ThreadService _threadService;
    private readonly ConfigLoader _configLoader;
    private readonly OutputWriter _output;
    private readonly ILogger<ThreadsCommands> _logger;

    public ThreadsCommands(
        GmailClientFactory gmailFactory,
        ThreadService threadService,
        ConfigLoader configLoader,
        OutputWriter output,
        ILogger<ThreadsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _threadService = threadService;
        _configLoader = configLoader;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List email threads from the mailbox. " +
        "A thread groups an original message and all its replies into one conversation. " +
        "Accepts the same --label, --query, --max, and --page-token flags as 'nr messages list'. " +
        "Use 'nr threads read <id>' to view all messages in a thread. " +
        "Examples: 'nr threads list' | 'nr threads list -q \"from:boss@company.com\" --json'")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('l', Description = "Filter by label ID or display name (default: INBOX). " +
            "System labels: INBOX, SENT, SPAM, TRASH, UNREAD, STARRED, DRAFT, IMPORTANT. " +
            "User labels accept their display name or ID (see 'nr labels list'). " +
            "Examples: -l UNREAD | -l SENT | -l \"Work/Projects\"")] string? label = null,
        [Option('q', Description = "Gmail search query — same syntax as the Gmail search box. " +
            "Examples: -q \"is:unread\" | -q \"from:alice@example.com\" | -q \"has:attachment subject:invoice\"")] string? query = null,
        [Option('n', Description = "Maximum number of threads to return (1–500). Default: 25. " +
            "Example: -n 50")] int? max = null,
        [Option("page-token", Description = "Pagination token from a previous list response. " +
            "When more results exist the output prints: 'Next page: nr threads list --page-token <token>'. " +
            "Example: --page-token 09169338489761460975")] string? pageToken = null)
    {
        var config = _configLoader.Load(globals.Config);
        var effectiveLabel = label ?? config.DefaultLabel;
        var effectiveMax = max ?? config.DefaultMaxResults;

        if (effectiveMax is < 1 or > 500)
        {
            _output.WriteError($"--max must be between 1 and 500 (got {effectiveMax}). Run 'nr threads list --help' for usage.");
            Environment.Exit(ExitCodes.InvalidArguments);
            return;
        }

        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "threads.list" });
            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
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
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "threads list failed");
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("read", Description = "Read all messages in a thread in chronological order. " +
        "Shows each message's From, Date, and decoded body. " +
        "Get thread IDs from 'nr threads list' or from the threadId field in 'nr messages list --json'. " +
        "Examples: 'nr threads read 19cb04f07919b8a7' | 'nr threads read <id> --json'")]
    public async Task ReadAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail thread ID to read. " +
            "Get IDs from 'nr threads list' or from the threadId field in 'nr messages list --json'.")] string id,
        [Option("format", Description = "Content format for each message in the thread. " +
            "'full' (default): decoded body text for every message. " +
            "'metadata': headers only for each message — no body fetched. " +
            "'minimal': IDs only, no headers or body. " +
            "Example: --format metadata")] string format = "full")
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "threads.read",
                ["ThreadId"] = id
            });

            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
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
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "threads read failed for {ThreadId}", id);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }
}
