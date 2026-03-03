namespace NorthernRange.Models;

public record AttachmentListResult(
    string MessageId,
    List<AttachmentInfo> Attachments);

public record AttachmentInfo(
    string AttachmentId,
    string Filename,
    string MimeType,
    long Size);

public record AttachmentDownloadResult(
    string MessageId,
    string AttachmentId,
    string Filename,
    string MimeType,
    long Size,
    string OutputPath);
