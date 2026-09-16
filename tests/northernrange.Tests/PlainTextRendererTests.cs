using NorthernRange.Output;
using Xunit;

namespace NorthernRange.Tests;

public class PlainTextRendererTests
{
    // ── DisplayWidth (wide/zero-width handling) ───────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("abc", 3)]
    [InlineData("中", 2)]                 // CJK ideograph → 2 columns
    [InlineData("中文", 4)]
    [InlineData("⭐", 2)]                 // emoji (U+2B50) → 2 columns
    [InlineData("a中b", 4)]
    public void DisplayWidth_CountsColumns(string s, int expected)
    {
        Assert.Equal(expected, PlainTextRenderer.DisplayWidth(s));
    }

    [Fact]
    public void DisplayWidth_CombiningMarks_AreZeroWidth()
    {
        // "e" + combining acute accent renders in one column.
        Assert.Equal(1, PlainTextRenderer.DisplayWidth("é"));
    }

    [Fact]
    public void DisplayWidth_ZeroWidthJoiner_IsZeroWidth()
    {
        Assert.Equal(0, PlainTextRenderer.DisplayWidth("‍"));
    }

    [Theory]
    [InlineData("﻿")]  // zero-width no-break space (BOM)
    [InlineData("‌")]  // zero-width non-joiner
    [InlineData("͏")]  // combining grapheme joiner
    [InlineData("­")]  // soft hyphen
    public void DisplayWidth_FormatAndCombiningChars_AreZeroWidth(string s)
    {
        // These appear in marketing-mail snippets and used to break table alignment.
        Assert.Equal(0, PlainTextRenderer.DisplayWidth(s));
        Assert.Equal(2, PlainTextRenderer.DisplayWidth("a" + s + "b"));
    }

    [Fact]
    public void StripInvisible_RemovesFormatAndControlChars_KeepsAccents()
    {
        var s = "͏ ‌ ﻿ Keep é and 中 ​";
        Assert.Equal("Keep é and 中", PlainTextRenderer.StripInvisible(s));
    }

    [Fact]
    public void Truncate_StripsInvisibleChars_BeforeMeasuring()
    {
        var snippet = "﻿‌﻿‌﻿‌Hello world";
        Assert.Equal("Hello world", PlainTextRenderer.Truncate(snippet, 11));
    }

    [Fact]
    public void RenderTable_AlignsColumns_WithInvisibleCharsInCells()
    {
        List<string[]> rows =
        [
            ["1", PlainTextRenderer.Truncate("͏ ‌ ﻿ ͏ ‌ ﻿ Zoom Scheduler", 20)],
            ["2", PlainTextRenderer.Truncate("plain snippet", 20)],
        ];
        var lines = PlainTextRenderer.RenderTable(["ID", "Snippet"], rows).Split('\n');
        var widths = lines.Select(PlainTextRenderer.DisplayWidth).Distinct().ToList();
        Assert.Single(widths);
    }

    // ── Truncate ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, 10, "")]
    [InlineData("", 10, "")]
    [InlineData("short", 10, "short")]
    [InlineData("exactly10!", 10, "exactly10!")]  // length == max, unchanged
    [InlineData("this is too long", 10, "this is...")]
    public void Truncate_Works(string? input, int max, string expected)
    {
        Assert.Equal(expected, PlainTextRenderer.Truncate(input, max));
    }

    [Fact]
    public void Truncate_AddsEllipsisAndRespectsMaxLength()
    {
        var result = PlainTextRenderer.Truncate("abcdefghij", 6);
        Assert.Equal("abc...", result);
        Assert.Equal(6, result.Length);
    }

    // ── FormatSize ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(5242880, "5.0 MB")]
    public void FormatSize_PicksUnit(long bytes, string expected)
    {
        Assert.Equal(expected, PlainTextRenderer.FormatSize(bytes));
    }

    // ── FormatDate ────────────────────────────────────────────────────────

    [Fact]
    public void FormatDate_Null_ReturnsEmpty()
    {
        Assert.Equal("", PlainTextRenderer.FormatDate(null, "iso8601"));
    }

    [Fact]
    public void FormatDate_Iso8601_IsUtcWithMarker()
    {
        var date = new DateTimeOffset(2026, 3, 1, 13, 45, 0, TimeSpan.Zero);
        Assert.Equal("2026-03-01 13:45 UTC", PlainTextRenderer.FormatDate(date, "iso8601"));
    }

    // ── RenderTable ───────────────────────────────────────────────────────

    [Fact]
    public void RenderTable_ContainsHeadersAndBorders()
    {
        var table = PlainTextRenderer.RenderTable(
            new[] { "ID", "Name" },
            new List<string[]> { new[] { "1", "Alice" }, new[] { "2", "Bob" } });

        Assert.Contains("ID", table);
        Assert.Contains("Alice", table);
        Assert.Contains("+", table);
        Assert.Contains("|", table);
    }

    [Fact]
    public void RenderTable_AlignsColumns_EvenWithWideChars()
    {
        // A wide CJK cell must not break column alignment: every rendered line
        // (borders + rows) must have the same display width.
        var table = PlainTextRenderer.RenderTable(
            new[] { "Name" },
            new List<string[]> { new[] { "中文" }, new[] { "ab" } });

        var lines = table.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        var widths = lines.Select(PlainTextRenderer.DisplayWidth).Distinct().ToList();
        Assert.Single(widths);
    }

    [Fact]
    public void RenderTable_HandlesRaggedRows()
    {
        // A row with fewer cells than headers must not throw.
        var table = PlainTextRenderer.RenderTable(
            new[] { "A", "B", "C" },
            new List<string[]> { new[] { "1" } });

        Assert.Contains("A", table);
    }
}
