using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using NorthernRange.Commands;
using NorthernRange.Config;
using Spectre.Console;

namespace NorthernRange.Output;

public class OutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public OutputMode DetermineMode(GlobalOptions globals, AppConfig config)
    {
        var envJson = Environment.GetEnvironmentVariable("NR_JSON") == "1";
        if (globals.Json || config.DefaultOutputFormat == "json" || envJson)
            return OutputMode.Json;
        if (globals.Ui && !Console.IsOutputRedirected)
            return OutputMode.RichUi;
        if (globals.Ui && Console.IsOutputRedirected)
            Console.Error.WriteLine("Warning: --ui flag ignored because stdout is redirected. Using plain text.");
        return OutputMode.PlainText;
    }

    public void WriteJson<T>(T value) where T : class
    {
        Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));
    }

    public void WritePlain(string text)
    {
        Console.WriteLine(text);
    }

    public void WriteTable(string[] headers, List<string[]> rows, OutputMode mode)
    {
        if (mode == OutputMode.RichUi)
        {
            var table = new Table();
            table.Border(TableBorder.Simple);
            foreach (var h in headers)
                table.AddColumn(new TableColumn(Markup.Escape(h)));
            foreach (var row in rows)
                table.AddRow(row.Select(c => Markup.Escape(c)).ToArray());
            AnsiConsole.Write(table);
        }
        else
        {
            Console.WriteLine(PlainTextRenderer.RenderTable(headers, rows));
        }
    }

    public void WriteKeyValue(IEnumerable<(string Key, string Value)> items, OutputMode mode)
    {
        var list = items.ToList();
        var maxKey = list.Count > 0 ? list.Max(x => x.Key.Length) : 0;

        if (mode == OutputMode.RichUi)
        {
            var grid = new Grid();
            grid.AddColumn(new GridColumn().NoWrap());
            grid.AddColumn();
            foreach (var (k, v) in list)
                grid.AddRow(new Markup($"[bold]{Markup.Escape(k + ":")}[/]"), new Text(v));
            AnsiConsole.Write(grid);
        }
        else
        {
            foreach (var (k, v) in list)
                Console.WriteLine($"{(k + ":").PadRight(maxKey + 2)} {v}");
        }
    }

    public void WriteError(string message)
    {
        Console.Error.WriteLine(message);
    }

    public void WriteDivider(string label = "")
    {
        if (string.IsNullOrEmpty(label))
            Console.WriteLine(new string('-', 60));
        else
            Console.WriteLine($"--- {label} ---");
    }
}
