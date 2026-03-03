using Google.Apis.Gmail.v1;
using Microsoft.Extensions.Logging;
using NorthernRange.Errors;
using NorthernRange.Models;

namespace NorthernRange.Gmail;

public class LabelService
{
    private readonly ILogger<LabelService> _logger;

    public LabelService(ILogger<LabelService> logger)
    {
        _logger = logger;
    }

    public async Task<LabelListResult> ListAsync(GmailService gmail, CancellationToken ct = default)
    {
        _logger.LogInformation("Listing labels");

        Google.Apis.Gmail.v1.Data.ListLabelsResponse resp;
        try
        {
            resp = await gmail.Users.Labels.List("me").ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex)
        {
            throw MapApiException(ex);
        }

        var labels = (resp.Labels ?? []).Select(MapLabel).ToList();
        return new LabelListResult(labels);
    }

    public async Task<LabelDetail> GetAsync(GmailService gmail, string idOrName, CancellationToken ct = default)
    {
        _logger.LogInformation("Getting label {LabelIdOrName}", idOrName);

        // If it looks like a label ID, try direct get first
        var resolvedId = idOrName;
        if (!IsLikelyLabelId(idOrName))
            resolvedId = await ResolveNameToIdAsync(gmail, idOrName, ct);

        Google.Apis.Gmail.v1.Data.Label label;
        try
        {
            label = await gmail.Users.Labels.Get("me", resolvedId).ExecuteAsync(ct);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Maybe it was a name, try resolving
            if (resolvedId == idOrName)
            {
                resolvedId = await ResolveNameToIdAsync(gmail, idOrName, ct);
                label = await gmail.Users.Labels.Get("me", resolvedId).ExecuteAsync(ct);
            }
            else throw new NrException(ExitCodes.NotFound, $"Label '{idOrName}' not found.");
        }
        catch (Google.GoogleApiException ex)
        {
            throw MapApiException(ex);
        }

        return MapLabel(label);
    }

    private async Task<string> ResolveNameToIdAsync(GmailService gmail, string name, CancellationToken ct)
    {
        var resp = await gmail.Users.Labels.List("me").ExecuteAsync(ct);
        var matches = (resp.Labels ?? [])
            .Where(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.Count switch
        {
            0 => throw new NrException(ExitCodes.NotFound, $"Label '{name}' not found."),
            > 1 => throw new NrException(ExitCodes.InvalidArguments,
                $"Label name '{name}' is ambiguous — multiple labels match. Use the label ID instead."),
            _ => matches[0].Id
        };
    }

    private static bool IsLikelyLabelId(string value) =>
        value.StartsWith("Label_", StringComparison.Ordinal)
        || value == value.ToUpperInvariant(); // System labels are all-caps

    private static LabelDetail MapLabel(Google.Apis.Gmail.v1.Data.Label l)
    {
        LabelColor? color = null;
        if (l.Color is not null)
            color = new LabelColor(l.Color.TextColor ?? "", l.Color.BackgroundColor ?? "");

        return new LabelDetail(
            l.Id,
            l.Name,
            l.Type ?? "user",
            l.MessagesTotal,
            l.MessagesUnread,
            l.ThreadsTotal,
            l.ThreadsUnread,
            color);
    }

    private static NrException MapApiException(Google.GoogleApiException ex) =>
        ex.HttpStatusCode switch
        {
            System.Net.HttpStatusCode.NotFound =>
                new NrException(ExitCodes.NotFound, $"Label not found: {ex.Error?.Message ?? ex.Message}"),
            System.Net.HttpStatusCode.Unauthorized =>
                new NrException(ExitCodes.AuthRequired, "Authentication expired. Run 'nr auth login'."),
            _ =>
                new NrException(ExitCodes.ApiError, $"Gmail API error ({(int)ex.HttpStatusCode}): {ex.Error?.Message ?? ex.Message}")
        };
}
