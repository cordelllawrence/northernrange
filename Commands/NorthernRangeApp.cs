using Cocona;

namespace NorthernRange.Commands;

[HasSubCommands(typeof(AuthCommands),        "auth",        Description = "Authentication: login, logout, status")]
[HasSubCommands(typeof(MessagesCommands),    "messages",    Description = "Messages: list, read, label, send, reply")]
[HasSubCommands(typeof(ThreadsCommands),     "threads",     Description = "Threads: list, read")]
[HasSubCommands(typeof(LabelsCommands),      "labels",      Description = "Labels: list, show, create, delete")]
[HasSubCommands(typeof(AttachmentsCommands), "attachments", Description = "Attachments: list, download")]
[HasSubCommands(typeof(DraftCommands),       "drafts",      Description = "Drafts: list, send, delete")]
public class NorthernRangeApp
{
}
