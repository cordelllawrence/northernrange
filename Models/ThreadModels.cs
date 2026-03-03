namespace NorthernRange.Models;

public record ThreadListResult(
    List<ThreadSummary> Threads,
    string? NextPageToken,
    long ResultSizeEstimate);

public record ThreadSummary(
    string Id,
    string? Snippet,
    int? MessageCount,
    string? HistoryId);

public record ThreadDetail(
    string Id,
    string? HistoryId,
    List<MessageDetail> Messages);
