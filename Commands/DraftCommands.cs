using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class DraftCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly SendService _sendService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<DraftCommands> _logger;

    public DraftCommands(
        GmailClientFactory gmailFactory,
        SendService sendService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<DraftCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _sendService  = sendService;
        _resolver     = resolver;
        _output       = output;
        _logger       = logger;
    }

    [Command("list", Description = "List saved drafts sorted newest-first. Use --json to get draft IDs.")]
    public async Task ListAsync(
        GlobalOptions globals,
        [Option('n', Description = "Max drafts to return (1–100). Default: 25.")] int max = 25)
    {
        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        if (max is < 1 or > 100)
        {
            _output.WriteError("--max must be between 1 and 100.");
            Environment.Exit(ExitCodes.InvalidArguments);
            return;
        }

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "drafts.list" });
            var gmail  = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
            var result = await _sendService.ListDraftsAsync(gmail, max);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(result);
                return;
            }

            if (result.Drafts.Count == 0)
            {
                _output.WritePlain("No drafts found.");
                return;
            }

            var dateFormat = ctx.Config.DateFormat;
            var headers    = new[] { "Draft ID", "Date", "To", "Subject", "Snippet" };
            var rows = result.Drafts.Select(d => new[]
            {
                d.DraftId,
                PlainTextRenderer.FormatDate(d.Date, dateFormat),
                PlainTextRenderer.Truncate(d.To, 30),
                PlainTextRenderer.Truncate(d.Subject, 40),
                PlainTextRenderer.Truncate(d.Snippet, 45)
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
            _logger.LogError(ex, "drafts list failed");
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("send", Description = "Send an existing draft immediately. Get draft IDs from 'nr drafts list --json'.")]
    public async Task SendAsync(
        GlobalOptions globals,
        [Argument(Description = "Draft ID to send. Get from 'nr drafts list' or 'nr drafts list --json'.")] string draftId)
    {
        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "drafts.send",
                ["DraftId"] = draftId
            });

            var gmail  = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
            var result = await _sendService.SendDraftAsync(gmail, draftId);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(result);
                return;
            }

            _output.WritePlain($"Sent.  Message-ID: {result.MessageId}  Thread: {result.ThreadId}");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "drafts send failed for {DraftId}", draftId);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("delete", Description = "Permanently delete a draft. Cannot be undone. Get draft IDs from 'nr drafts list --json'.")]
    public async Task DeleteAsync(
        GlobalOptions globals,
        [Argument(Description = "Draft ID to delete. Get from 'nr drafts list' or 'nr drafts list --json'.")] string draftId)
    {
        var ctx = _resolver.Resolve(globals);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "drafts.delete",
                ["DraftId"] = draftId
            });

            var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
            await _sendService.DeleteDraftAsync(gmail, draftId);
            _output.WritePlain($"Draft {draftId} deleted.");
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "drafts delete failed for {DraftId}", draftId);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }
}
