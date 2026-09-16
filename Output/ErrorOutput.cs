using System.Text.Encodings.Web;
using System.Text.Json;

namespace NorthernRange.Output;

/// <summary>
/// The single place errors are written. Always stderr, never stdout, so
/// stdout stays parseable. In JSON mode the line is a JSON envelope
/// <c>{"error":{"code":N,"message":"…"}}</c>; otherwise plain text.
/// </summary>
public static class ErrorOutput
{
    /// <summary>Set from the <c>--json</c> flag / <c>NR_JSON</c> at startup and from config once loaded.</summary>
    public static bool JsonMode { get; set; }

    // Keep quotes and non-ASCII readable; stderr is for humans and agents, not HTML.
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void Write(int exitCode, string message)
    {
        if (JsonMode)
        {
            var envelope = new { error = new { code = exitCode, message } };
            Console.Error.WriteLine(JsonSerializer.Serialize(envelope, Options));
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }
}
