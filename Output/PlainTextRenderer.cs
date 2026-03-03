using System.Text;

namespace NorthernRange.Output;

public static class PlainTextRenderer
{
    public static string RenderTable(string[] headers, List<string[]> rows)
    {
        var colCount = headers.Length;
        var widths = new int[colCount];

        for (var i = 0; i < colCount; i++)
            widths[i] = headers[i].Length;

        foreach (var row in rows)
            for (var i = 0; i < Math.Min(colCount, row.Length); i++)
                widths[i] = Math.Max(widths[i], row[i].Length);

        var sep = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";
        var sb = new StringBuilder();

        sb.AppendLine(sep);
        sb.AppendLine("|" + string.Join("|", headers.Select((h, i) => $" {h.PadRight(widths[i])} ")) + "|");
        sb.AppendLine(sep);

        foreach (var row in rows)
        {
            var cells = Enumerable.Range(0, colCount)
                .Select(i => i < row.Length ? row[i] : "");
            sb.AppendLine("|" + string.Join("|", cells.Select((c, i) => $" {c.PadRight(widths[i])} ")) + "|");
        }

        sb.Append(sep);
        return sb.ToString();
    }

    public static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }

    public static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024):F1} MB"
        };
    }

    public static string FormatDate(DateTimeOffset? date, string dateFormat)
    {
        if (date == null) return "";
        return dateFormat == "local"
            ? date.Value.LocalDateTime.ToString("ddd, dd MMM yyyy HH:mm")
            : date.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC";
    }
}
