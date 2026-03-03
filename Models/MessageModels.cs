namespace NorthernRange.Models;

public record MessageListResult(
    List<MessageSummary> Messages,
    string? NextPageToken,
    long ResultSizeEstimate);

public record MessageSummary(
    string Id,
    string? ThreadId,
    string? From,
    string? To,
    string? Subject,
    DateTimeOffset? Date,
    string? Snippet);

public record MessageDetail(
    string Id,
    string? ThreadId,
    List<string> LabelIds,
    Dictionary<string, string> Headers,
    string? Snippet,
    MessageBody? Body,
    List<AttachmentInfo> Attachments,
    DateTimeOffset InternalDate,
    long SizeEstimate);

public record MessageBody(
    string MimeType,
    string? Text);
