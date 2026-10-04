using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Models;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class LabelsCommands
{
    private readonly LabelService _labelService;
    private readonly CommandPrelude _prelude;
    private readonly OutputWriter _output;
    private readonly ILogger<LabelsCommands> _logger;

    public LabelsCommands(
        LabelService labelService,
        CommandPrelude prelude,
        OutputWriter output,
        ILogger<LabelsCommands> logger)
    {
        _labelService = labelService;
        _prelude = prelude;
        _output = output;
        _logger = logger;
    }

    [ErrorHandlingFilter]
    [Command("list", Description = "List all labels, system and user-created. Use the IDs with --label on list commands.")]
    public async Task ListAsync(GlobalOptions globals)
    {
        using var session = _prelude.Begin(globals, _logger, "labels.list");

        var gmail = await session.GmailAsync();
        var result = await _labelService.ListAsync(gmail);

        if (session.Mode == OutputMode.Json)
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

        _output.WriteTable(headers, rows, session.Mode);
    }

    [ErrorHandlingFilter]
    [Command("show", Description = "Show one label: counts and color. Accepts a label ID or display name. Get IDs from 'nr labels list'.")]
    public async Task ShowAsync(
        GlobalOptions globals,
        [Argument(Description = "Label ID or display name. Get IDs from 'nr labels list'.")] string id)
    {
        using var session = _prelude.Begin(globals, _logger, "labels.show",
            ("LabelId", id));

        var gmail = await session.GmailAsync();
        var label = await _labelService.GetAsync(gmail, id);

        if (session.Mode == OutputMode.Json)
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

        _output.WriteKeyValue(items, session.Mode);
    }

    [ErrorHandlingFilter]
    [Command("create", Description = "Create a user label. Optionally set text and background colors as hex, both together or neither.")]
    public async Task CreateAsync(
        GlobalOptions globals,
        [Argument(Description = "Display name for the new label.")] string name,
        [Option("text-color", Description = "Text color hex (e.g. '#ffffff'). Requires --bg-color.")] string? textColor = null,
        [Option("bg-color", Description = "Background color hex (e.g. '#4986e7'). Requires --text-color.")] string? bgColor = null)
    {
        if ((textColor is null) != (bgColor is null))
            throw new NrException(ExitCodes.InvalidArguments,
                "Colors must be given as a pair. Use both --text-color and --bg-color, or neither.");

        using var session = _prelude.Begin(globals, _logger, "labels.create",
            ("LabelName", name));

        var gmail = await session.GmailAsync();
        var label = await _labelService.CreateAsync(gmail, name, textColor, bgColor);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(label);
            return;
        }

        _output.WritePlain($"Created label '{label.Name}' ({label.Id}).");
    }

    [ErrorHandlingFilter]
    [Command("delete", Description = "Delete a user label. Messages are kept; only the label is removed. Get IDs from 'nr labels list'.")]
    public async Task DeleteAsync(
        GlobalOptions globals,
        [Argument(Description = "Label ID or display name. Get IDs from 'nr labels list'.")] string id)
    {
        using var session = _prelude.Begin(globals, _logger, "labels.delete",
            ("LabelId", id));

        var gmail = await session.GmailAsync();
        await _labelService.DeleteAsync(gmail, id);

        if (session.Mode == OutputMode.Json)
        {
            _output.WriteJson(new DeleteResult(true, id));
            return;
        }

        _output.WritePlain($"Deleted label '{id}'.");
    }
}
