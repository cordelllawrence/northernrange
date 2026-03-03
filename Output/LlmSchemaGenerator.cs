using System.Diagnostics.CodeAnalysis;
using System.Text;

#pragma warning disable IL2070 // Reflection on Type parameters — safe in partial trim mode

namespace NorthernRange.Output;

/// <summary>
/// Converts C# record/class types into JSON Schema dictionaries (for JSON output)
/// and human-readable JSON-shaped examples (for Markdown output) using reflection.
/// </summary>
public static class LlmSchemaGenerator
{
    /// <summary>
    /// Generate a JSON-Schema-compatible dictionary for a CLR type.
    /// Handles primitives, nullable, List&lt;T&gt;, Dictionary, and nested records.
    /// </summary>
    public static Dictionary<string, object> GenerateSchema(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null)
        {
            var inner = GenerateSchema(underlying);
            inner["nullable"] = true;
            return inner;
        }

        if (type == typeof(string))
            return new Dictionary<string, object> { ["type"] = "string" };

        if (type == typeof(bool))
            return new Dictionary<string, object> { ["type"] = "boolean" };

        if (type == typeof(int) || type == typeof(long))
            return new Dictionary<string, object> { ["type"] = "integer" };

        if (type == typeof(DateTimeOffset))
            return new Dictionary<string, object> { ["type"] = "string", ["format"] = "date-time" };

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var itemType = type.GetGenericArguments()[0];
            return new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = GenerateSchema(itemType)
            };
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var valueType = type.GetGenericArguments()[1];
            return new Dictionary<string, object>
            {
                ["type"] = "object",
                ["additionalProperties"] = GenerateSchema(valueType)
            };
        }

        // Treat as object — enumerate properties via constructor params (records)
        // or public properties (plain classes)
        var props = new Dictionary<string, object>();
        var ctors = type.GetConstructors();
        if (ctors.Length > 0 && ctors[0].GetParameters().Length > 0)
        {
            foreach (var p in ctors[0].GetParameters())
                props[CamelCase(p.Name!)] = GenerateSchema(p.ParameterType);
        }
        else
        {
            foreach (var p in type.GetProperties())
                props[CamelCase(p.Name)] = GenerateSchema(p.PropertyType);
        }

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = props
        };
    }

    /// <summary>
    /// Render a JSON-shaped example showing the structure of a response type.
    /// Uses type annotations instead of concrete values (e.g. "string", 0, true).
    /// </summary>
    public static string RenderSchemaExample(Type type)
    {
        var sb = new StringBuilder();
        RenderValue(sb, type, 0);
        return sb.ToString();
    }

    private static void RenderValue(StringBuilder sb, Type type, int indent)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var isNullable = underlying is not null;
        var effective = underlying ?? type;

        if (effective == typeof(string))
        {
            sb.Append(isNullable ? "\"string | null\"" : "\"string\"");
            return;
        }
        if (effective == typeof(bool))
        {
            sb.Append(isNullable ? "bool | null" : "bool");
            return;
        }
        if (effective == typeof(int) || effective == typeof(long))
        {
            sb.Append(isNullable ? "0 | null" : "0");
            return;
        }
        if (effective == typeof(DateTimeOffset))
        {
            sb.Append(isNullable ? "\"ISO-8601 | null\"" : "\"ISO-8601\"");
            return;
        }

        if (effective.IsGenericType && effective.GetGenericTypeDefinition() == typeof(List<>))
        {
            var itemType = effective.GetGenericArguments()[0];
            if (IsSimpleType(itemType))
            {
                sb.Append('[');
                RenderValue(sb, itemType, indent);
                sb.Append(']');
            }
            else
            {
                sb.AppendLine("[");
                Indent(sb, indent + 1);
                RenderValue(sb, itemType, indent + 1);
                sb.AppendLine();
                Indent(sb, indent);
                sb.Append(']');
            }
            return;
        }

        if (effective.IsGenericType && effective.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            var valType = effective.GetGenericArguments()[1];
            sb.Append("{ \"key\": ");
            RenderValue(sb, valType, indent);
            sb.Append(" }");
            return;
        }

        // Object — enumerate fields
        var ctors = effective.GetConstructors();
        var parameters = ctors.Length > 0 && ctors[0].GetParameters().Length > 0
            ? ctors[0].GetParameters().Select(p => (Name: CamelCase(p.Name!), Type: p.ParameterType)).ToArray()
            : effective.GetProperties().Select(p => (Name: CamelCase(p.Name), Type: p.PropertyType)).ToArray();

        sb.AppendLine("{");
        for (var i = 0; i < parameters.Length; i++)
        {
            var (name, pType) = parameters[i];
            Indent(sb, indent + 1);
            sb.Append($"\"{name}\": ");
            RenderValue(sb, pType, indent + 1);
            if (i < parameters.Length - 1)
                sb.Append(',');
            sb.AppendLine();
        }
        Indent(sb, indent);
        sb.Append('}');
    }

    private static bool IsSimpleType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(string) || t == typeof(int) || t == typeof(long)
            || t == typeof(bool) || t == typeof(DateTimeOffset);
    }

    private static void Indent(StringBuilder sb, int level)
    {
        sb.Append(new string(' ', level * 2));
    }

    private static string CamelCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
