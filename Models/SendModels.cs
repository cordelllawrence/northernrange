namespace NorthernRange.Models;

public record SendResult(
    string MessageId,
    string? ThreadId,
    string Subject,
    List<string> To,
    bool IsDraft,
    string? DraftId);
