using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Filters;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

[ErrorHandlingFilter]
public class SendCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly SendService _sendService;
    private readonly AccountResolver _resolver;
    private readonly OutputWriter _output;
    private readonly ILogger<SendCommands> _logger;

    public SendCommands(
        GmailClientFactory gmailFactory,
        SendService sendService,
        AccountResolver resolver,
        OutputWriter output,
        ILogger<SendCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _sendService  = sendService;
        _resolver     = resolver;
        _output       = output;
        _logger       = logger;
    }

    [Command("new", Description = "Compose and send a new message. Body from --body, --body-file, or stdin. Use --draft to save instead.")]
    public async Task NewAsync(
        GlobalOptions globals,
        [Option('t', Description = "Recipient address. Repeat for multiple. Required.")] List<string>? to = null,
        [Option('c', Description = "CC address. Repeat for multiple.")] List<string>? cc = null,
        [Option("bcc", Description = "BCC address. Repeat for multiple.")] List<string>? bcc = null,
        [Option('s', Description = "Subject line. Required.")] string subject = "",
        [Option("body", Description = "Body text inline. Falls back to --body-file then stdin if omitted.")] string? body = null,
        [Option("body-file", Description = "Path to a plain-text file whose contents become the body.")] string? bodyFile = null,
        [Option('a', Description = "Path to a local file to attach. Repeat for multiple.")] List<string>? attach = null,
        [Option("draft", Description = "Save as a draft instead of sending immediately.")] bool draft = false)
    {
        if (to is null || to.Count == 0)
            throw new NrException(ExitCodes.InvalidArguments, "At least one --to / -t recipient is required.");
        if (string.IsNullOrWhiteSpace(subject))
            throw new NrException(ExitCodes.InvalidArguments, "--subject / -s is required.");

        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"] = "send.new",
            ["To"]      = string.Join(", ", to)
        });

        var bodyText = await ResolveBodyAsync(body, bodyFile);
        var gmail    = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var result   = await _sendService.SendNewAsync(
            gmail, to, cc, bcc, subject, bodyText, attach, draft);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        if (result.IsDraft)
            _output.WritePlain($"Draft saved.  Draft-ID: {result.DraftId}");
        else
            _output.WritePlain($"Sent.  Message-ID: {result.MessageId}  Thread: {result.ThreadId}");
    }

    [Command("reply", Description = "Reply to an existing message. Threading headers set automatically. Get IDs from 'nr messages list'.")]
    public async Task ReplyAsync(
        GlobalOptions globals,
        [Argument(Description = "Gmail message ID to reply to. Get from 'nr messages list'.")] string messageId,
        [Option("body", Description = "Reply body text. Falls back to --body-file then stdin if omitted.")] string? body = null,
        [Option("body-file", Description = "Path to a plain-text file whose contents become the reply body.")] string? bodyFile = null,
        [Option('a', Description = "Path to a local file to attach. Repeat for multiple.")] List<string>? attach = null,
        [Option("reply-all", Description = "CC all original recipients (To + Cc) in addition to the sender.")] bool replyAll = false,
        [Option("draft", Description = "Save as a draft instead of sending immediately.")] bool draft = false)
    {
        var ctx  = _resolver.Resolve(globals);
        var mode = _output.DetermineMode(globals, ctx.Config);

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["Command"]   = "send.reply",
            ["MessageId"] = messageId
        });

        var bodyText = await ResolveBodyAsync(body, bodyFile);
        var gmail    = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath);
        var result   = await _sendService.SendReplyAsync(
            gmail, messageId, bodyText, attach, replyAll, draft);

        if (mode == OutputMode.Json)
        {
            _output.WriteJson(result);
            return;
        }

        if (result.IsDraft)
            _output.WritePlain($"Draft saved.  Draft-ID: {result.DraftId}");
        else
            _output.WritePlain($"Sent.  Message-ID: {result.MessageId}  Thread: {result.ThreadId}");
    }

    // Resolves body text from --body, --body-file, or stdin (in that order).
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
            "Body required: use --body, --body-file, or pipe content to stdin.");
    }
}
