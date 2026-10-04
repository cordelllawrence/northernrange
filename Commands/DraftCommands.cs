using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Models;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class DraftCommands
{
    private readonly SendService _sendService;
    private readonly CommandPrelude _prelude;
    private readonly OutputWriter _output;
    private readonly ILogger<DraftCommands> _logger;

    public DraftCommands(
        SendService sendService,
        CommandPrelude prelude,
        OutputWriter output,
        ILogger<DraftCommands> logger)
    {
        _sendService = sendService;
        _prelude     = prelude;
        _output      = output;
        _logger      = logger;
    }

    [ErrorHandlingFilter]
    [Command("list", Description = "List saved drafts in Gmail's order. Use --json to get draft IDs.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('n', Description = "Max drafts to return (1-500). Default: 25.")] int max = 25,
        [Option("page-token", Description = "Pagination token from a previous list response.")] string? pageToken = null)
    {
        ParamValidation.RequireRange(max, 1, 500, "max");

        using var session = _prelude.Begin(globals, _logger, "drafts.list");

        var gmail  = await session.GmailAsync();
        var result = await _sendService.ListDraftsAsync(gmail, max, pageToken);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        // Gmail can return an empty page that still carries a token, so the
        // hint is printed whenever a token exists, not only after a table.
        if (result.Drafts.Count == 0)
        {
            _output.WritePlain(result.NextPageToken is null ? "No drafts found." : "No drafts on this page.");
        }
        else
        {
            var dateFormat = session.Config.DateFormat;
            var headers    = new[] { "Draft ID", "Date", "To", "Subject", "Snippet" };
            var rows = result.Drafts.Select(d => new[]
            {
                d.DraftId,
                PlainTextRenderer.FormatDate(d.Date, dateFormat),
                PlainTextRenderer.Truncate(d.To, 30),
                PlainTextRenderer.Truncate(d.Subject, 40),
                PlainTextRenderer.Truncate(d.Snippet, 45)
            }).ToList();

            _output.WriteTable(headers, rows, session.Mode);
        }

        if (!string.IsNullOrEmpty(result.NextPageToken))
            _output.WritePlain(NextPageHint.Build("drafts list", result.NextPageToken, globals,
                ("--max", max == 25 ? null : max.ToString())));
    }

    [ErrorHandlingFilter]
    [Command("send", Description = "Send a saved draft now. Get draft IDs from 'nr drafts list --json'.")]
    public async Task SendAsync(
        GlobalOptions globals,
        [Argument(Description = "Draft ID to send. Get from 'nr drafts list' or 'nr drafts list --json'.")] string draftId)
    {
        using var session = _prelude.Begin(globals, _logger, "drafts.send",
            ("DraftId", draftId));

        var gmail  = await session.GmailAsync();
        var result = await _sendService.SendDraftAsync(gmail, draftId);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        _output.WritePlain($"Sent message {result.MessageId} (thread {result.ThreadId}).");
    }

    [ErrorHandlingFilter]
    [Command("delete", Description = "Delete a draft permanently. Get draft IDs from 'nr drafts list --json'.")]
    public async Task DeleteAsync(
        GlobalOptions globals,
        [Argument(Description = "Draft ID to delete. Get from 'nr drafts list' or 'nr drafts list --json'.")] string draftId)
    {
        using var session = _prelude.Begin(globals, _logger, "drafts.delete",
            ("DraftId", draftId));

        var gmail = await session.GmailAsync();
        await _sendService.DeleteDraftAsync(gmail, draftId);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(new DeleteResult(true, draftId));
            return;
        }

        _output.WritePlain($"Deleted draft {draftId}.");
    }
}
