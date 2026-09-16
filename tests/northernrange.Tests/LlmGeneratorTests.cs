using System.Text.Json;
using NorthernRange.Models;
using NorthernRange.Output;
using Xunit;

namespace NorthernRange.Tests;

public class LlmSchemaGeneratorTests
{
    [Fact]
    public void GenerateSchema_Record_ProducesObjectWithProperties()
    {
        var schema = LlmSchemaGenerator.GenerateSchema(typeof(MessageListResult));

        Assert.Equal("object", schema["type"]);
        var props = Assert.IsType<Dictionary<string, object>>(schema["properties"]);
        Assert.True(props.ContainsKey("messages"));
        Assert.True(props.ContainsKey("nextPageToken"));
        Assert.True(props.ContainsKey("resultSizeEstimate"));

        var messages = Assert.IsType<Dictionary<string, object>>(props["messages"]);
        Assert.Equal("array", messages["type"]);

        var count = Assert.IsType<Dictionary<string, object>>(props["resultSizeEstimate"]);
        Assert.Equal("integer", count["type"]);
    }

    [Fact]
    public void GenerateSchema_NullableValueType_IsMarkedNullable()
    {
        var schema = LlmSchemaGenerator.GenerateSchema(typeof(MessageSummary));
        var props = Assert.IsType<Dictionary<string, object>>(schema["properties"]);

        // DateTimeOffset? Date → nullable date-time string
        var date = Assert.IsType<Dictionary<string, object>>(props["date"]);
        Assert.Equal("string", date["type"]);
        Assert.Equal("date-time", date["format"]);
        Assert.True((bool)date["nullable"]);
    }

    [Fact]
    public void GenerateSchema_Dictionary_UsesAdditionalProperties()
    {
        var schema = LlmSchemaGenerator.GenerateSchema(typeof(MessageDetail));
        var props = Assert.IsType<Dictionary<string, object>>(schema["properties"]);
        var headers = Assert.IsType<Dictionary<string, object>>(props["headers"]);
        Assert.Equal("object", headers["type"]);
        Assert.True(headers.ContainsKey("additionalProperties"));
    }

    [Fact]
    public void RenderSchemaExample_ProducesJsonShape()
    {
        var example = LlmSchemaGenerator.RenderSchemaExample(typeof(AttachmentDownloadResult));
        Assert.StartsWith("{", example.TrimStart());
        Assert.Contains("\"filename\"", example);
        Assert.Contains("\"outputPath\"", example);
    }
}

public class LlmDocGeneratorTests
{
    [Fact]
    public void ConciseMarkdown_ListsAllCommandGroups()
    {
        var md = LlmDocGenerator.GenerateConciseMarkdown();

        Assert.Contains("## Commands", md);
        foreach (var cmd in new[]
        {
            "nr auth login", "nr messages list", "nr messages read", "nr threads list",
            "nr labels create", "nr attachments download", "nr messages send", "nr drafts send",
        })
            Assert.Contains(cmd, md);
    }

    [Fact]
    public void ConciseMarkdown_Filter_RestrictsToGroup()
    {
        var md = LlmDocGenerator.GenerateConciseMarkdown(new[] { "messages" });
        Assert.Contains("nr messages", md);
        Assert.DoesNotContain("nr drafts", md);
        Assert.DoesNotContain("nr send", md);
    }

    [Fact]
    public void JsonToolSchema_IsValidJson_WithCommandsAndExitCodes()
    {
        using var doc = JsonDocument.Parse(LlmDocGenerator.GenerateJsonToolSchema());
        var root = doc.RootElement;

        Assert.Equal("nr", root.GetProperty("name").GetString());
        Assert.True(root.GetProperty("commands").GetArrayLength() > 0);
        Assert.Equal("Success", root.GetProperty("exitCodes").GetProperty("0").GetString());
    }

    [Fact]
    public void JsonToolSchema_DraftsList_ExposesPageToken()
    {
        // Regression for the L1 fix: drafts list must advertise --page-token.
        using var doc = JsonDocument.Parse(LlmDocGenerator.GenerateJsonToolSchema());
        var draftsList = doc.RootElement.GetProperty("commands").EnumerateArray()
            .First(c => c.GetProperty("name").GetString() == "drafts list");

        var optionNames = draftsList.GetProperty("options").EnumerateArray()
            .Select(o => o.GetProperty("name").GetString()).ToList();

        Assert.Contains("--page-token", optionNames);
    }

    [Fact]
    public void JsonToolSchema_IncludesMutatingCommands()
    {
        using var doc = JsonDocument.Parse(LlmDocGenerator.GenerateJsonToolSchema());
        var names = doc.RootElement.GetProperty("commands").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString()).ToList();

        Assert.Contains("messages reply", names);
        Assert.Contains("labels delete", names);
        Assert.Contains("messages label", names);
    }

    [Fact]
    public void JsonToolSchema_EveryCommandHasAResponseSchema()
    {
        // The response-type map in LlmDocGenerator is hand-maintained. A command
        // without an entry silently documents nothing; this catches that drift.
        using var doc = JsonDocument.Parse(LlmDocGenerator.GenerateJsonToolSchema());
        var missing = doc.RootElement.GetProperty("commands").EnumerateArray()
            .Where(c => !c.TryGetProperty("response", out var r) || r.ValueKind == JsonValueKind.Null)
            .Select(c => c.GetProperty("name").GetString())
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void JsonToolSchema_ExitCodes_MatchExitCodesClass()
    {
        using var doc = JsonDocument.Parse(LlmDocGenerator.GenerateJsonToolSchema());
        var documented = doc.RootElement.GetProperty("exitCodes").EnumerateObject()
            .Select(p => int.Parse(p.Name)).OrderBy(x => x).ToList();
        var actual = typeof(NorthernRange.Errors.ExitCodes).GetFields()
            .Select(f => (int)f.GetRawConstantValue()!).OrderBy(x => x).ToList();

        Assert.Equal(actual, documented);
    }

    [Theory]
    [InlineData(typeof(MessageListResult))]
    [InlineData(typeof(MessageDetail))]
    [InlineData(typeof(LabelDetail))]
    [InlineData(typeof(DeleteResult))]
    [InlineData(typeof(SendResult))]
    public void GenerateSchema_PropertyNames_MatchSerializedOutput(Type type)
    {
        // The schema is what agents read; the serializer is what they parse.
        // Both must agree on camelCase property names.
        var schema = LlmSchemaGenerator.GenerateSchema(type);
        var schemaProps = Assert.IsType<Dictionary<string, object>>(schema["properties"]).Keys.OrderBy(k => k).ToList();

        var serializerProps = type.GetProperties()
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).OrderBy(k => k).ToList();

        Assert.Equal(serializerProps, schemaProps);
    }

    [Fact]
    public void FullMarkdown_IncludesConfigAndSchemas()
    {
        var md = LlmDocGenerator.GenerateFullMarkdown();
        Assert.Contains("## Configuration", md);
        Assert.Contains("JSON output", md);
        Assert.Contains("## Exit Codes", md);
    }
}
