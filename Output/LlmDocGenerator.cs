using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Cocona;
using NorthernRange.Commands;
using NorthernRange.Models;

#pragma warning disable IL2070 // Reflection on Type parameters — safe in partial trim mode

namespace NorthernRange.Output;

/// <summary>
/// Generates AI-consumable documentation for the CLI by reflecting on
/// Cocona command classes. Supports concise Markdown (--llm), comprehensive
/// Markdown (--llm-full), and JSON tool schema (--llm --json).
/// </summary>
public static class LlmDocGenerator
{
    // ── Internal data structures ──────────────────────────────────────

    private record CommandGroupInfo(string Name, string Description, List<SubcommandInfo> Subcommands);
    private record SubcommandInfo(string Name, string Description, List<OptionInfo> Options, List<ArgumentInfo> Arguments, Type? ResponseType);
    private record OptionInfo(string LongName, char? ShortName, string Description, string TypeName, string? DefaultValue, bool IsRequired, bool IsRepeatable);
    private record ArgumentInfo(string Name, string Description, string TypeName, bool IsRequired);

    // ── Response type mapping ─────────────────────────────────────────

    private static readonly Dictionary<(string Group, string Command), Type?> ResponseTypes = new()
    {
        { ("auth", "login"), typeof(AuthLoginResult) },
        { ("auth", "logout"), typeof(AuthLogoutResult) },
        { ("auth", "status"), typeof(AuthStatusResult) },
        { ("messages", "list"), typeof(MessageListResult) },
        { ("messages", "read"), typeof(MessageDetail) },
        { ("threads", "list"), typeof(ThreadListResult) },
        { ("threads", "read"), typeof(ThreadDetail) },
        { ("labels", "list"), typeof(LabelListResult) },
        { ("labels", "info"), typeof(LabelDetail) },
        { ("attachments", "list"), typeof(AttachmentListResult) },
        { ("attachments", "download"), typeof(AttachmentDownloadResult) },
        { ("send", "new"), typeof(SendResult) },
        { ("send", "reply"), typeof(SendResult) },
        { ("drafts", "list"), typeof(DraftListResult) },
        { ("drafts", "send"), typeof(SendResult) },
        { ("drafts", "delete"), null },
    };

    // ── Public API ────────────────────────────────────────────────────

    public static string GenerateConciseMarkdown(string[]? filter = null)
    {
        var version = GetVersion();
        var groups = ApplyFilter(ReflectCommandGroups(), filter);
        var globalOpts = ReflectGlobalOptions();
        var sb = new StringBuilder();

        sb.AppendLine($"# nr (northernrange) v{version}");
        sb.AppendLine();
        sb.AppendLine("Gmail CLI for scripts, AI agents, and automated pipelines.");
        sb.AppendLine();
        sb.AppendLine("## Prerequisites");
        sb.AppendLine();
        sb.AppendLine("Requires OAuth2 credentials (`client_secrets.json`) and a one-time `nr auth login`.");
        sb.AppendLine();

        // Usage
        sb.AppendLine("## Usage");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("nr <command> <subcommand> [options]");
        sb.AppendLine("```");
        sb.AppendLine();

        // Global options — compact list
        sb.AppendLine("## Global Options");
        sb.AppendLine();
        foreach (var opt in globalOpts)
        {
            var flag = opt.ShortName.HasValue
                ? $"`-{opt.ShortName.Value}`, `--{opt.LongName}`"
                : $"`--{opt.LongName}`";
            sb.AppendLine($"- {flag} — {opt.Description}");
        }
        sb.AppendLine();

        // Commands table
        sb.AppendLine("## Commands");
        sb.AppendLine();
        sb.AppendLine("| Command | Description |");
        sb.AppendLine("|---|---|");
        foreach (var group in groups)
        {
            foreach (var cmd in group.Subcommands)
            {
                var args = cmd.Arguments.Count > 0
                    ? " " + string.Join(" ", cmd.Arguments.Select(a => a.IsRequired ? $"<{a.Name}>" : $"[{a.Name}]"))
                    : "";
                var opts = cmd.Options.Count > 0
                    ? " " + string.Join(" ", cmd.Options.Select(o =>
                        o.ShortName.HasValue ? $"[-{o.ShortName.Value}]" : $"[--{o.LongName}]"))
                    : "";
                sb.AppendLine($"| `nr {group.Name} {cmd.Name}{args}{opts}` | {cmd.Description} |");
            }
        }
        sb.AppendLine();

        // Exit codes — single line
        sb.AppendLine("## Exit Codes");
        sb.AppendLine();
        sb.AppendLine("0=Success, 1=Error, 2=InvalidArgs, 3=AuthRequired, 4=ApiError, 5=NotFound, 6=FileError");
        sb.AppendLine();

        sb.AppendLine("Use `--json` on any command for machine-readable JSON output.");
        sb.AppendLine("Use `nr --llm-full` for comprehensive docs with JSON schemas.");

        return sb.ToString().TrimEnd();
    }

    public static string GenerateFullMarkdown(string[]? filter = null)
    {
        var version = GetVersion();
        var groups = ApplyFilter(ReflectCommandGroups(), filter);
        var globalOpts = ReflectGlobalOptions();
        var sb = new StringBuilder();

        sb.AppendLine($"# nr — northernrange v{version}");
        sb.AppendLine();
        sb.AppendLine("Gmail CLI for scripts, AI agents, and automated pipelines.");
        sb.AppendLine();
        sb.AppendLine("## Prerequisites");
        sb.AppendLine();
        sb.AppendLine("1. Create a Google Cloud OAuth2 Desktop client ID.");
        sb.AppendLine("2. Download `client_secrets.json` and place it in the default config directory");
        sb.AppendLine("   (`%APPDATA%\\northernrange\\` on Windows, `~/.config/northernrange/` on macOS/Linux),");
        sb.AppendLine("   or pass `--credentials <path>`.");
        sb.AppendLine("3. Run `nr auth login` once to authenticate via browser.");
        sb.AppendLine();

        // Usage
        sb.AppendLine("## Usage");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("nr <command> <subcommand> [options]");
        sb.AppendLine("```");
        sb.AppendLine();

        // Global options table
        sb.AppendLine("## Global Options");
        sb.AppendLine();
        sb.AppendLine("| Option | Type | Default | Description |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var opt in globalOpts)
        {
            var flag = opt.ShortName.HasValue
                ? $"`-{opt.ShortName.Value}`, `--{opt.LongName}`"
                : $"`--{opt.LongName}`";
            sb.AppendLine($"| {flag} | {opt.TypeName} | {opt.DefaultValue ?? "-"} | {opt.Description} |");
        }
        sb.AppendLine();

        // Per-command sections
        foreach (var group in groups)
        {
            sb.AppendLine($"---");
            sb.AppendLine();
            sb.AppendLine($"## {group.Name}");
            sb.AppendLine();

            foreach (var cmd in group.Subcommands)
            {
                var args = cmd.Arguments.Count > 0
                    ? " " + string.Join(" ", cmd.Arguments.Select(a => a.IsRequired ? $"<{a.Name}>" : $"[{a.Name}]"))
                    : "";
                sb.AppendLine($"### `nr {group.Name} {cmd.Name}{args}`");
                sb.AppendLine();
                sb.AppendLine(cmd.Description);
                sb.AppendLine();

                // Arguments
                if (cmd.Arguments.Count > 0)
                {
                    sb.AppendLine("**Arguments:**");
                    sb.AppendLine();
                    foreach (var arg in cmd.Arguments)
                    {
                        var req = arg.IsRequired ? "Required." : "Optional.";
                        sb.AppendLine($"- `<{arg.Name}>` ({arg.TypeName}) — {arg.Description} {req}");
                    }
                    sb.AppendLine();
                }

                // Options table
                if (cmd.Options.Count > 0)
                {
                    sb.AppendLine("**Options:**");
                    sb.AppendLine();
                    sb.AppendLine("| Option | Type | Default | Description |");
                    sb.AppendLine("|---|---|---|---|");
                    foreach (var opt in cmd.Options)
                    {
                        var flag = opt.ShortName.HasValue
                            ? $"`-{opt.ShortName.Value}`, `--{opt.LongName}`"
                            : $"`--{opt.LongName}`";
                        var type = opt.IsRepeatable ? $"{opt.TypeName}[]" : opt.TypeName;
                        sb.AppendLine($"| {flag} | {type} | {opt.DefaultValue ?? "-"} | {opt.Description} |");
                    }
                    sb.AppendLine();
                }

                // JSON output schema
                if (cmd.ResponseType is not null)
                {
                    sb.AppendLine("**JSON output** (`--json`):");
                    sb.AppendLine();
                    sb.AppendLine("```json");
                    sb.AppendLine(LlmSchemaGenerator.RenderSchemaExample(cmd.ResponseType));
                    sb.AppendLine("```");
                    sb.AppendLine();
                }
            }
        }

        // Configuration
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Configuration");
        sb.AppendLine();
        sb.AppendLine("### config.json");
        sb.AppendLine();
        sb.AppendLine("| Key | Type | Default | Description |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine("| `defaultLabel` | string | `INBOX` | Default label for list commands. |");
        sb.AppendLine("| `defaultMaxResults` | int | `25` | Default page size (1\u2013500). |");
        sb.AppendLine("| `defaultOutputFormat` | string | `text` | `text` or `json`. |");
        sb.AppendLine("| `dateFormat` | string | `iso8601` | `iso8601` or `local`. |");
        sb.AppendLine("| `credentialsPath` | string | null | Override path to `client_secrets.json`. |");
        sb.AppendLine("| `httpTimeoutSeconds` | int | `30` | HTTP request timeout. |");
        sb.AppendLine();
        sb.AppendLine("### Environment Variables");
        sb.AppendLine();
        sb.AppendLine("| Variable | Description |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| `NR_JSON` | Set to `1` for JSON output. |");
        sb.AppendLine("| `NR_DEFAULT_LABEL` | Override default label. |");
        sb.AppendLine("| `NR_MAX_RESULTS` | Override default max results. |");
        sb.AppendLine("| `NR_CREDENTIALS` | Override credentials path. |");
        sb.AppendLine("| `NR_CONFIG` | Override config file path. |");
        sb.AppendLine();

        // Exit codes
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## Exit Codes");
        sb.AppendLine();
        sb.AppendLine("| Code | Meaning |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| 0 | Success |");
        sb.AppendLine("| 1 | General error |");
        sb.AppendLine("| 2 | Invalid arguments |");
        sb.AppendLine("| 3 | Authentication required (`nr auth login`) |");
        sb.AppendLine("| 4 | API error |");
        sb.AppendLine("| 5 | Not found |");
        sb.AppendLine("| 6 | File error |");

        return sb.ToString().TrimEnd();
    }

    public static string GenerateJsonToolSchema(string[]? filter = null)
    {
        var version = GetVersion();
        var groups = ApplyFilter(ReflectCommandGroups(), filter);
        var globalOpts = ReflectGlobalOptions();

        var commands = new List<Dictionary<string, object?>>();
        foreach (var group in groups)
        {
            foreach (var cmd in group.Subcommands)
            {
                var entry = new Dictionary<string, object?>
                {
                    ["name"] = $"{group.Name} {cmd.Name}",
                    ["description"] = cmd.Description,
                    ["arguments"] = cmd.Arguments.Select(a => new Dictionary<string, object?>
                    {
                        ["name"] = a.Name,
                        ["type"] = a.TypeName,
                        ["required"] = a.IsRequired,
                        ["description"] = a.Description,
                    }).ToList(),
                    ["options"] = cmd.Options.Select(o => new Dictionary<string, object?>
                    {
                        ["name"] = $"--{o.LongName}",
                        ["shortName"] = o.ShortName.HasValue ? $"-{o.ShortName.Value}" : null,
                        ["type"] = o.IsRepeatable ? $"{o.TypeName}[]" : o.TypeName,
                        ["required"] = o.IsRequired,
                        ["default"] = o.DefaultValue,
                        ["repeatable"] = o.IsRepeatable ? true : null,
                        ["description"] = o.Description,
                    }).ToList(),
                    ["response"] = cmd.ResponseType is not null
                        ? LlmSchemaGenerator.GenerateSchema(cmd.ResponseType)
                        : null,
                };
                commands.Add(entry);
            }
        }

        var schema = new Dictionary<string, object?>
        {
            ["name"] = "nr",
            ["version"] = version,
            ["description"] = "Gmail CLI for scripts, AI agents, and automated pipelines",
            ["globalOptions"] = globalOpts.Select(o => new Dictionary<string, object?>
            {
                ["name"] = $"--{o.LongName}",
                ["shortName"] = o.ShortName.HasValue ? $"-{o.ShortName.Value}" : null,
                ["type"] = o.TypeName,
                ["required"] = o.IsRequired,
                ["default"] = o.DefaultValue,
                ["description"] = o.Description,
            }).ToList(),
            ["commands"] = commands,
            ["exitCodes"] = new Dictionary<string, string>
            {
                ["0"] = "Success",
                ["1"] = "General error",
                ["2"] = "Invalid arguments",
                ["3"] = "Authentication required",
                ["4"] = "API error",
                ["5"] = "Not found",
                ["6"] = "File error",
            },
        };

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };

        return JsonSerializer.Serialize(schema, jsonOptions);
    }

    // ── Filtering ──────────────────────────────────────────────────────

    /// <summary>
    /// Filter reflected command groups by optional args.
    /// "messages" → only the messages group. "messages read" → only that subcommand.
    /// Empty/null → no filtering.
    /// </summary>
    private static List<CommandGroupInfo> ApplyFilter(List<CommandGroupInfo> groups, string[]? filter)
    {
        if (filter is null || filter.Length == 0)
            return groups;

        var groupName = filter[0];
        var subcommandName = filter.Length > 1 ? filter[1] : null;

        var matched = groups.Where(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matched.Count == 0)
            return groups; // no match — return all rather than empty

        if (subcommandName is not null)
        {
            matched = matched.Select(g =>
            {
                var filtered = g.Subcommands
                    .Where(s => s.Name.Equals(subcommandName, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                return filtered.Count > 0 ? g with { Subcommands = filtered } : g;
            }).ToList();
        }

        return matched;
    }

    // ── Reflection core ───────────────────────────────────────────────

    private static List<CommandGroupInfo> ReflectCommandGroups()
    {
        var appType = typeof(NorthernRangeApp);
        var groups = new List<CommandGroupInfo>();

        foreach (var attr in appType.GetCustomAttributes<HasSubCommandsAttribute>())
        {
            var groupName = attr.CommandName ?? attr.Type.Name;
            var description = attr.Description ?? "";
            var subcommands = ReflectSubcommands(attr.Type, groupName);
            groups.Add(new CommandGroupInfo(groupName, description, subcommands));
        }

        groups.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
        return groups;
    }

    private static List<SubcommandInfo> ReflectSubcommands(Type commandClass, string groupName)
    {
        var subcommands = new List<SubcommandInfo>();
        var methods = commandClass.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        foreach (var method in methods)
        {
            var cmdAttr = method.GetCustomAttribute<CommandAttribute>();
            if (cmdAttr is null) continue;

            var cmdName = cmdAttr.Name ?? method.Name;
            var cmdDesc = cmdAttr.Description ?? "";
            var options = new List<OptionInfo>();
            var arguments = new List<ArgumentInfo>();

            foreach (var param in method.GetParameters())
            {
                if (param.ParameterType == typeof(GlobalOptions))
                    continue;

                var optAttr = param.GetCustomAttribute<OptionAttribute>();
                if (optAttr is not null)
                {
                    var longName = optAttr.Name ?? ToKebabCase(param.Name!);
                    var shortName = optAttr.ShortNames.Count > 0 ? optAttr.ShortNames[0] : (char?)null;
                    var isRepeatable = param.ParameterType.IsGenericType
                        && param.ParameterType.GetGenericTypeDefinition() == typeof(List<>);
                    var typeName = FormatTypeName(param.ParameterType);
                    var defaultValue = FormatDefault(param);
                    var isRequired = !param.HasDefaultValue;

                    options.Add(new OptionInfo(longName, shortName, optAttr.Description ?? "", typeName, defaultValue, isRequired, isRepeatable));
                    continue;
                }

                var argAttr = param.GetCustomAttribute<ArgumentAttribute>();
                if (argAttr is not null)
                {
                    var argName = argAttr.Name ?? param.Name!;
                    var typeName = FormatTypeName(param.ParameterType);
                    var isRequired = !param.HasDefaultValue;

                    arguments.Add(new ArgumentInfo(argName, argAttr.Description ?? "", typeName, isRequired));
                }
            }

            ResponseTypes.TryGetValue((groupName, cmdName), out var responseType);
            subcommands.Add(new SubcommandInfo(cmdName, cmdDesc, options, arguments, responseType));
        }

        return subcommands;
    }

    private static List<OptionInfo> ReflectGlobalOptions()
    {
        var options = new List<OptionInfo>();
        var ctor = typeof(GlobalOptions).GetConstructors()[0];

        foreach (var param in ctor.GetParameters())
        {
            var optAttr = param.GetCustomAttribute<OptionAttribute>();
            if (optAttr is null) continue;

            var longName = optAttr.Name ?? ToKebabCase(param.Name!);
            var shortName = optAttr.ShortNames.Count > 0 ? optAttr.ShortNames[0] : (char?)null;
            var typeName = FormatTypeName(param.ParameterType);
            var defaultValue = FormatDefault(param);

            options.Add(new OptionInfo(longName, shortName, optAttr.Description ?? "", typeName, defaultValue, false, false));
        }

        return options;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static string GetVersion()
    {
        return Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
    }

    private static string FormatTypeName(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return FormatTypeName(underlying);

        if (type == typeof(string)) return "string";
        if (type == typeof(int) || type == typeof(long)) return "int";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(DateTimeOffset)) return "string (ISO-8601)";

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            return FormatTypeName(type.GetGenericArguments()[0]);

        return type.Name;
    }

    private static string? FormatDefault(ParameterInfo param)
    {
        if (!param.HasDefaultValue) return null;
        var val = param.DefaultValue;
        if (val is null) return "null";
        if (val is bool b) return b ? "true" : "false";
        if (val is string s) return s.Length == 0 ? "\"\"" : s;
        return val.ToString();
    }

    private static string ToKebabCase(string name)
    {
        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0)
            {
                sb.Append('-');
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }
        return sb.ToString();
    }
}
