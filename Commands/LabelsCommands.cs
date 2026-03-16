using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

[ErrorHandlingFilter]
public class LabelsCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly LabelService _labelService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<LabelsCommands> _logger;

    public LabelsCommands(
        GmailClientFactory gmailFactory,
        LabelService labelService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<LabelsCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _labelService = labelService;
        _resolver = resolver;
        _output = output;
        _logger = logger;
    }

    [Command("list", Description = "List all labels (system and user-created). Use IDs with 'nr messages list --label'.")]
    public async Task ListAsync(GlobalOptions globals)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "labels.list" });
        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
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

    [Command("info", Description = "Show details for a single label: counts and color. Accepts label ID or display name.")]
    public async Task InfoAsync(
        GlobalOptions globals,
        [Argument(Description = "Label ID or display name. Get IDs from 'nr labels list'.")] string id)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "labels.info",
            ["LabelId"] = id
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
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

    [Command("create", Description = "Create a new user label. Optionally set text and background colors (hex, e.g. '#000000').")]
    public async Task CreateAsync(
        GlobalOptions globals,
        [Argument(Description = "Display name for the new label.")] string name,
        [Option("text-color", Description = "Text color hex (e.g. '#ffffff'). Requires --bg-color.")] string? textColor = null,
        [Option("bg-color", Description = "Background color hex (e.g. '#4986e7'). Requires --text-color.")] string? bgColor = null)
    {
        if ((textColor is null) != (bgColor is null))
            throw new NrException(ExitCodes.InvalidArguments,
                "Both --text-color and --bg-color must be provided together, or neither.");

        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "labels.create",
            ["LabelName"] = name
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var label = await _labelService.CreateAsync(gmail, name, textColor, bgColor);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(label);
            return;
        }

        _output.WritePlain($"Created label '{label.Name}'  ID: {label.Id}");
    }

    [Command("delete", Description = "Delete a user label. Messages with this label are NOT deleted, only the label is removed.")]
    public async Task DeleteAsync(
        GlobalOptions globals,
        [Argument(Description = "Label ID or display name. Get IDs from 'nr labels list'.")] string id)
    {
        var ctx = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "labels.delete",
            ["LabelId"] = id
        });

        var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        await _labelService.DeleteAsync(gmail, id);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(new { deleted = true, id });
            return;
        }

        _output.WritePlain($"Deleted label '{id}'.");
    }
}
