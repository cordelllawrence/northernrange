using Cocona;
using Microsoft.Extensions.Logging;
using NorthernRange.Config;
using NorthernRange.Errors;
using NorthernRange.Gmail;
using NorthernRange.Output;

namespace NorthernRange.Commands;

public class SendCommands
{
    private readonly GmailClientFactory _gmailFactory;
    private readonly SendService _sendService;
    private readonly ConfigLoader _configLoader;
    private readonly OutputWriter _output;
    private readonly ILogger<SendCommands> _logger;

    public SendCommands(
        GmailClientFactory gmailFactory,
        SendService sendService,
        ConfigLoader configLoader,
        OutputWriter output,
        ILogger<SendCommands> logger)
    {
        _gmailFactory = gmailFactory;
        _sendService  = sendService;
        _configLoader = configLoader;
        _output       = output;
        _logger       = logger;
    }

    [Command("new", Description =
        "Compose and send a new email message. " +
        "Requires --to and --subject. " +
        "Body text comes from --body, --body-file, or stdin (in that priority order). " +
        "Add --draft to save as a draft instead of sending immediately. " +
        "Attach local files with --attach (repeat the flag for multiple files). " +
        "Examples: " +
        "'nr send new --to alice@example.com --subject \"Hello\" --body \"Hi there\"' | " +
        "'nr send new -t alice@example.com -t bob@example.com -s \"Report\" --body-file report.txt --attach report.pdf' | " +
        "'echo \"Body text\" | nr send new -t alice@example.com -s \"Piped body\"' | " +
        "'nr send new -t self@example.com -s \"Draft\" --body \"WIP\" --draft'")]
    public async Task NewAsync(
        GlobalOptions globals,
        [Option('t', Description =
            "Recipient email address. Repeat the flag for multiple recipients: -t a@b.com -t c@d.com. Required."
        )] List<string>? to = null,
        [Option('c', Description =
            "CC email address. Repeat for multiple: --cc a@b.com --cc c@d.com."
        )] List<string>? cc = null,
        [Option("bcc", Description =
            "BCC email address. Repeat for multiple. Recipients are hidden from each other."
        )] List<string>? bcc = null,
        [Option('s', Description =
            "Email subject line. Required. Example: -s \"Weekly report\""
        )] string? subject = null,
        [Option("body", Description =
            "Message body as inline text. If omitted, falls back to --body-file then stdin. " +
            "Example: --body \"Hello, just checking in.\""
        )] string? body = null,
        [Option("body-file", Description =
            "Path to a plain-text file whose contents become the message body. " +
            "Takes priority over stdin. Example: --body-file ~/drafts/email.txt"
        )] string? bodyFile = null,
        [Option('a', Description =
            "Path to a local file to attach. Repeat for multiple attachments: -a file1.pdf -a image.png. " +
            "Example: -a ~/reports/q1.pdf"
        )] List<string>? attach = null,
        [Option("draft", Description =
            "Save as a draft instead of sending immediately. " +
            "The draft appears in Gmail Drafts and can be sent later with 'nr drafts send <draft-id>'. " +
            "Example: --draft"
        )] bool draft = false)
    {
        var config   = _configLoader.Load(globals.Config);
        var credPath = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        // Validate required inputs
        if (to is null || to.Count == 0)
        {
            _output.WriteError("At least one --to / -t recipient is required.");
            Environment.Exit(ExitCodes.InvalidArguments);
            return;
        }
        if (string.IsNullOrWhiteSpace(subject))
        {
            _output.WriteError("--subject / -s is required.");
            Environment.Exit(ExitCodes.InvalidArguments);
            return;
        }

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"] = "send.new",
                ["To"]      = string.Join(", ", to)
            });

            var bodyText = await ResolveBodyAsync(body, bodyFile);
            var gmail    = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
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
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "send new failed");
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
    }

    [Command("reply", Description =
        "Reply to an existing message. Subject, threading headers (In-Reply-To, References), " +
        "and the thread ID are set automatically from the original message. " +
        "By default replies only to the sender; use --reply-all to CC all original recipients. " +
        "Body text comes from --body, --body-file, or stdin. " +
        "Add --draft to save the reply as a draft instead of sending. " +
        "Examples: " +
        "'nr send reply 19cb08f9253d9482 --body \"Thanks, sounds good.\"' | " +
        "'nr send reply <msg-id> --reply-all --body \"See attached\" --attach report.pdf' | " +
        "'nr send reply <msg-id> --body \"WIP reply\" --draft'")]
    public async Task ReplyAsync(
        GlobalOptions globals,
        [Argument(Description =
            "Gmail message ID to reply to. " +
            "Get it from 'nr messages list' or 'nr messages list --json'."
        )] string messageId,
        [Option("body", Description =
            "Reply body text. If omitted, falls back to --body-file then stdin."
        )] string? body = null,
        [Option("body-file", Description =
            "Path to a plain-text file whose contents become the reply body."
        )] string? bodyFile = null,
        [Option('a', Description =
            "Path to a local file to attach. Repeat for multiple: -a file1.pdf -a image.png."
        )] List<string>? attach = null,
        [Option("reply-all", Description =
            "CC all original recipients (To and Cc of the original message) in addition to replying to the sender."
        )] bool replyAll = false,
        [Option("draft", Description =
            "Save the reply as a draft instead of sending immediately."
        )] bool draft = false)
    {
        var config    = _configLoader.Load(globals.Config);
        var credPath  = globals.Credentials ?? config.CredentialsPath ?? AppPaths.GetClientSecretsPath();
        var tokenPath = AppPaths.GetTokenStorePath();
        var mode = _output.DetermineMode(globals, config);

        try
        {
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["Command"]   = "send.reply",
                ["MessageId"] = messageId
            });

            var bodyText = await ResolveBodyAsync(body, bodyFile);
            var gmail    = await _gmailFactory.GetServiceAsync(credPath, tokenPath);
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
        catch (NrException ex)
        {
            _output.WriteError(ex.Message);
            Environment.Exit(ex.ExitCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "send reply failed for {MessageId}", messageId);
            _output.WriteError($"Unexpected error: {ex.Message}");
            Environment.Exit(ExitCodes.GeneralError);
        }
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
