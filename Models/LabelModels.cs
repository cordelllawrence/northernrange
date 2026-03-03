namespace NorthernRange.Models;

public record LabelListResult(List<LabelDetail> Labels);

public record LabelDetail(
    string Id,
    string Name,
    string Type,
    long? MessagesTotal,
    long? MessagesUnread,
    long? ThreadsTotal,
    long? ThreadsUnread,
    LabelColor? Color);

public record LabelColor(
    string TextColor,
    string BackgroundColor);
