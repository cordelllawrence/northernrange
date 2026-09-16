namespace NorthernRange.Models;

/// <summary>JSON result of every delete command: <c>{"deleted":true,"id":"…"}</c>.</summary>
public record DeleteResult(bool Deleted, string Id);
