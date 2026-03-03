namespace NorthernRange.Models;

public record DraftSummary(
    string DraftId,
    string? MessageId,
    string? Subject,
    string? To,
    string? Snippet,
    DateTimeOffset? Date);

public record DraftListResult(
    List<DraftSummary> Drafts,
    string? NextPageToken,
    long ResultSizeEstimate);
