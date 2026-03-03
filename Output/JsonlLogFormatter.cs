using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Serilog.Events;
using Serilog.Formatting;

namespace NorthernRange.Output;

/// <summary>
/// Writes one JSON object per line (JSONL) for AI-agent-readable log output.
/// Fields: t (timestamp), l (level), src (source context), msg (rendered message),
/// err (exception), plus all structured properties as top-level keys.
/// </summary>
public class JsonlLogFormatter : ITextFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public void Format(LogEvent logEvent, TextWriter output)
    {
        var obj = new Dictionary<string, object?>
        {
            ["t"] = logEvent.Timestamp.ToString("o"),
            ["l"] = LevelToString(logEvent.Level),
            ["msg"] = logEvent.RenderMessage(),
        };

        // Add source context as "src" if present
        if (logEvent.Properties.TryGetValue("SourceContext", out var sc))
            obj["src"] = sc.ToString().Trim('"');

        // Flatten all other properties as top-level keys
        foreach (var (key, value) in logEvent.Properties)
        {
            if (key == "SourceContext") continue;
            obj[key] = value switch
            {
                ScalarValue sv => sv.Value,
                _ => value.ToString().Trim('"')
            };
        }

        if (logEvent.Exception is not null)
            obj["err"] = logEvent.Exception.ToString();

        output.WriteLine(JsonSerializer.Serialize(obj, JsonOptions));
    }

    private static string LevelToString(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "FTL",
        _ => level.ToString()[..3].ToUpperInvariant()
    };
}
