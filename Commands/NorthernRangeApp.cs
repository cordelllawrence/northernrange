using Cocona;

namespace NorthernRange.Commands;

[HasSubCommands(typeof(AuthCommands), "auth", Description = "Authentication commands (login, logout, status)")]
[HasSubCommands(typeof(MessagesCommands), "messages", Description = "Email message commands (list, read)")]
[HasSubCommands(typeof(ThreadsCommands), "threads", Description = "Thread commands (list, read)")]
[HasSubCommands(typeof(LabelsCommands), "labels", Description = "Label commands (list, info)")]
[HasSubCommands(typeof(AttachmentsCommands), "attachments", Description = "Attachment commands (list, download)")]
[HasSubCommands(typeof(SendCommands),        "send",        Description = "Send messages and replies (send new, send reply)")]
[HasSubCommands(typeof(DraftCommands),       "drafts",      Description = "Manage draft messages (drafts list, drafts send, drafts delete)")]
public class NorthernRangeApp
{
}
