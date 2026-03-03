using System.Text;

namespace NorthernRange.Output;

public static class PlainTextRenderer
{
    public static string RenderTable(string[] headers, List<string[]> rows)
    {
        var colCount = headers.Length;
        var widths = new int[colCount];

        for (var i = 0; i < colCount; i++)
            widths[i] = DisplayWidth(headers[i]);

        foreach (var row in rows)
            for (var i = 0; i < Math.Min(colCount, row.Length); i++)
                widths[i] = Math.Max(widths[i], DisplayWidth(row[i]));

        var sep = "+" + string.Join("+", widths.Select(w => new string('-', w + 2))) + "+";
        var sb = new StringBuilder();

        sb.AppendLine(sep);
        sb.AppendLine("|" + string.Join("|", headers.Select((h, i) => " " + PadRight(h, widths[i]) + " ")) + "|");
        sb.AppendLine(sep);

        foreach (var row in rows)
        {
            var cells = Enumerable.Range(0, colCount)
                .Select(i => i < row.Length ? row[i] : "");
            sb.AppendLine("|" + string.Join("|", cells.Select((c, i) => " " + PadRight(c, widths[i]) + " ")) + "|");
        }

        sb.Append(sep);
        return sb.ToString();
    }

    // Returns the visual (terminal column) width of a string.
    // Wide characters such as emoji and CJK ideographs occupy 2 columns each.
    public static int DisplayWidth(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        var width = 0;
        foreach (var rune in s.EnumerateRunes())
            width += RuneWidth(rune.Value);
        return width;
    }

    // Pads a string to the given display width (not char count).
    private static string PadRight(string s, int displayWidth)
    {
        var padding = Math.Max(0, displayWidth - DisplayWidth(s));
        return padding == 0 ? s : s + new string(' ', padding);
    }

    private static int RuneWidth(int cp)
    {
        // Zero-width: C0/C1 controls, combining diacritics, variation selectors, ZWJ etc.
        if (cp is <= 0x1F or (>= 0x7F and <= 0x9F)) return 0;
        if (cp is >= 0x0300 and <= 0x036F) return 0;    // Combining diacritical marks
        if (cp is >= 0xFE00 and <= 0xFE0F) return 0;    // Variation selectors
        if (cp is >= 0x200B and <= 0x200F) return 0;    // Zero-width space/joiners

        // Wide (2 columns): emoji, CJK, Hangul, fullwidth forms
        if (cp is (>= 0x1100 and <= 0x115F)            // Hangul Jamo
               or (>= 0x231A and <= 0x231B)            // ⌚⌛ watch, hourglass
               or (>= 0x23E9 and <= 0x23FA)            // ⏩-⏺ AV/clock buttons
               or (>= 0x25AA and <= 0x25AB)            // ▪▫ small squares
               or (>= 0x25B6 and <= 0x25B6)            // ▶ play
               or (>= 0x25C0 and <= 0x25C0)            // ◀ reverse
               or (>= 0x25FB and <= 0x25FE)            // ◻-◾ medium squares
               or (>= 0x2600 and <= 0x27BF)            // ☀✨⚡✅❌ Misc Symbols + Dingbats
               or (>= 0x2934 and <= 0x2935)            // ⤴⤵ curved arrows
               or (>= 0x2B05 and <= 0x2B07)            // ⬅⬆⬇ arrows
               or (>= 0x2B1B and <= 0x2B1C)            // ⬛⬜ large squares
               or (>= 0x2B50 and <= 0x2B50)            // ⭐ star
               or (>= 0x2B55 and <= 0x2B55)            // ⭕ circle
               or (>= 0x2E80 and <= 0x3247)            // CJK Radicals, Kangxi, etc.
               or (>= 0x3250 and <= 0x4DBF)            // CJK Bopomofo, Extension A
               or (>= 0x4E00 and <= 0xA4C6)            // CJK Unified Ideographs
               or (>= 0xA960 and <= 0xA97C)            // Hangul Jamo Extended-A
               or (>= 0xAC00 and <= 0xD7A3)            // Hangul Syllables
               or (>= 0xF900 and <= 0xFAFF)            // CJK Compatibility Ideographs
               or (>= 0xFE10 and <= 0xFE1F)            // Vertical Forms
               or (>= 0xFE30 and <= 0xFE6F)            // CJK Compatibility Forms
               or (>= 0xFF01 and <= 0xFF60)            // Fullwidth ASCII
               or (>= 0xFFE0 and <= 0xFFE6)            // Fullwidth signs
               or (>= 0x1B000 and <= 0x1B001)          // Kana Supplement
               or (>= 0x1F004 and <= 0x1F004)          // Mahjong tile
               or (>= 0x1F0CF and <= 0x1F0CF)          // Playing card
               or (>= 0x1F1E0 and <= 0x1F1FF)          // Regional indicators (flags)
               or (>= 0x1F200 and <= 0x1F251)          // Enclosed Ideographic Supplement
               or (>= 0x1F300 and <= 0x1FAF8)          // Misc symbols, emoticons, pictographs
               or (>= 0x20000 and <= 0x2FFFD)          // CJK Extensions B-F
               or (>= 0x30000 and <= 0x3FFFD))         // CJK Extension G+
            return 2;

        return 1;
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
