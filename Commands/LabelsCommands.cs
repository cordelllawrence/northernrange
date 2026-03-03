using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class LabelsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly LabelService _labelService;
    private readonly ConfigLoader _configLoader;
    private readonly OutputWriter _output;
    private readonly ILogger<LabelsCommands> _logger;

    public LabelsCommands(
        GmailClientFactory gmailFactory,
        LabelService labelService,
        ConfigLoader configLoader,
        OutputWriter output,
        ILogger<LabelsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _labelService = labelService;
        _configLoader = configLoader;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List all labels in the mailbox — both Gmail system labels " +
        "(INBOX, SENT, SPAM, TRASH, UNREAD, STARRED, DRAFT, IMPORTANT, CATEGORY_*) " +
        "and any user-created labels. " +
        "Use the label ID or name with 'nr messages list --label' to filter messages. " +
        "Use 'nr labels info <id>' to see message counts and color for a specific label. " +
        "Examples: 'nr labels list' | 'nr labels list --json'")]
    public async Task ListAsync(GlobalOptions globals)
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "labels.list" });
            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
            var result = await _labelService.ListAsync(gmail);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(result);
                return;
            }

            var headers = new[] { "ID", "Name", "Type", "Total", "Unread" };
            var rows = result.Labels.Select(l => new[]
            {
                l.Id,
                l.Name,
                l.Type,
                l.MessagesTotal?.ToString() ?? "-",
                l.MessagesUnread?.ToString() ?? "-"
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
            _logger.LogError(ex, "labels list failed");
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("info", Description = "Show detailed information for a single label: " +
        "message counts, thread counts, unread counts, and color settings for user labels. " +
        "Accepts either a label ID (e.g. INBOX, Label_18) or a display name (e.g. 'Work/Projects'). " +
        "If a display name matches more than one label, the command exits with code 2. " +
        "Examples: 'nr labels info INBOX' | 'nr labels info \"Financial Updates\"' | 'nr labels info Label_18 --json'")]
    public async Task InfoAsync(
        GlobalOptions globals,
        [Argument(Description = "Label ID (e.g. INBOX, SENT, Label_18) or display name (e.g. 'Financial Updates'). " +
            "Label IDs and names are shown by 'nr labels list'.")] string id)
    {
        var config = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "labels.info",
                ["LabelId"] = id
            });

            var gmail = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
            var label = await _labelService.GetAsync(gmail, id);

            if (mode == OutputMode.Json)
            {
                _output.WriteJson(label);
                return;
            }

            var items = new List<(string, string)>
            {
                ("ID", label.Id),
                ("Name", label.Name),
                ("Type", label.Type),
                ("Messages Total", label.MessagesTotal?.ToString() ?? "-"),
                ("Messages Unread", label.MessagesUnread?.ToString() ?? "-"),
                ("Threads Total", label.ThreadsTotal?.ToString() ?? "-"),
                ("Threads Unread", label.ThreadsUnread?.ToString() ?? "-")
            };

            if (label.Color is not null)
            {
                items.Add(("Text Color", label.Color.TextColor));
                items.Add(("Background", label.Color.BackgroundColor));
            }

            _output.WriteKeyValue(items, mode);
        }
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "labels info failed for {LabelId}", id);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }
}
