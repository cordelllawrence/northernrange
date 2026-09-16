using Cocona;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Output;

namespace NorthernRange.Commands;

/// <summary>
/// <c>nr messages send</c> and <c>nr messages reply</c>.
/// </summary>
public partial class MessagesCommands
{
    [ErrorHandlingFilter]
    [Command("send", Description = "Compose and send a new message. Body from --body, --body-file, or stdin. Use --draft to save instead of sending.")]
    public async Task SendAsync(
        GlobalOptions globals,
        [Option('t', Description = "Recipient address. Repeat for multiple.")] List<string> to,
        [Option('s', Description = "Subject line.")] string subject,
        [Option('c', Description = "CC address. Repeat for multiple.")] List<string>? cc = null,
        [Option("bcc", Description = "BCC address. Repeat for multiple.")] List<string>? bcc = null,
        [Option("body", Description = "Body text inline. Falls back to --body-file, then stdin.")] string? body = null,
        [Option("body-file", Description = "Path to a plain-text file whose contents become the body.")] string? bodyFile = null,
        [Option('a', Description = "Path to a local file to attach. Repeat for multiple.")] List<string>? attach = null,
        [Option("draft", Description = "Save as a draft instead of sending.")] bool draft = false)
    {
        // --to and --subject are required at the parser level, so --help and
        // the --llm schema show them as required. Cocona still allows -s "".
        if (string.IsNullOrWhiteSpace(subject))
            throw new NrException(ExitCodes.InvalidArguments, "Subject is empty. Use --subject <text>.");

        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "messages.send",
            ["To"]      = string.Join(", ", to)
        });

        var bodyText = await ResolveBodyAsync(body, bodyFile);
        var gmail    = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath, ctx.Config.HttpTimeoutSeconds);
        var result   = await _sendService.SendNewAsync(
            gmail, to, cc, bcc, subject, bodyText, attach, draft);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        _output.WritePlain(result.IsDraft
            ? $"Saved draft {result.DraftId}."
            : $"Sent message {result.MessageId} (thread {result.ThreadId}).");
    }

    [ErrorHandlingFilter]
    [Command("reply", Description = "Reply to a message. Subject and threading headers are set automatically. Use --draft to save instead of sending. Get IDs from 'nr messages list'.")]
    public async Task ReplyAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID to reply to. Get from 'nr messages list'.")] string messageId,
        [Option("body", Description = "Reply body text. Falls back to --body-file, then stdin.")] string? body = null,
        [Option("body-file", Description = "Path to a plain-text file whose contents become the reply body.")] string? bodyFile = null,
        [Option('a', Description = "Path to a local file to attach. Repeat for multiple.")] List<string>? attach = null,
        [Option("reply-all", Description = "CC all original recipients (To + Cc) in addition to the sender.")] bool replyAll = false,
        [Option("draft", Description = "Save as a draft instead of sending.")] bool draft = false)
    {
        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"]   = "messages.reply",
            ["MessageId"] = messageId
        });

        var bodyText = await ResolveBodyAsync(body, bodyFile);
        var gmail    = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath, ctx.Config.HttpTimeoutSeconds);
        var result   = await _sendService.SendReplyAsync(
            gmail, messageId, bodyText, attach, replyAll, draft);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        _output.WritePlain(result.IsDraft
            ? $"Saved draft {result.DraftId}."
            : $"Sent message {result.MessageId} (thread {result.ThreadId}).");
    }

    // Resolves body text from --body-file, --body, or stdin (in that order).
    private static async Task<string> ResolveBodyAsync(string? body, string? bodyFile)
    {
        if (bodyFile is not null)
        {
            if (!File.Exists(bodyFile))
                throw new NrException(ExitCodes.FileError, $"Body file not found: {bodyFile}");
            return await File.ReadAllTextAsync(bodyFile);
        }
        if (body is not null)
            return body;
        if (Console.IsInputRedirected)
            return await Console.In.ReadToEndAsync();
        throw new NrException(
            ExitCodes.InvalidArguments,
            "No body given. Use --body <text>, --body-file <path>, or pipe the body to stdin.");
    }
}
