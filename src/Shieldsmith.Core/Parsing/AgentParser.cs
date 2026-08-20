using System.Text.Json;
using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads Copilot Studio agents.
///
/// Agents are NOT declared in solution.xml: a real agent export's RootComponents
/// list contains only its tables. They are found by the presence of a
/// bots/&lt;schemaname&gt;/bot.xml folder, and their behaviour lives in
/// botcomponents/&lt;schemaname&gt;.&lt;kind&gt;.&lt;Name&gt;/ as an XML descriptor plus a
/// payload file literally named "data" with no extension, holding YAML.
///
/// The YAML is read with a small targeted reader rather than a full YAML
/// parser: only a handful of fields matter for documentation, and the payloads
/// are machine-generated with a stable shape.
/// </summary>
public static class AgentParser
{
    public static void Parse(string rootPath, SolutionModel model)
    {
        var botsDirectory = Path.Combine(rootPath, "bots");
        if (!Directory.Exists(botsDirectory)) return;

        foreach (var directory in Directory.EnumerateDirectories(botsDirectory).OrderBy(d => d))
        {
            var botFile = Path.Combine(directory, "bot.xml");
            if (!File.Exists(botFile)) continue;

            try
            {
                var agent = ReadBot(botFile, Path.GetFileName(directory));
                ReadConfiguration(Path.Combine(directory, "configuration.json"), agent);
                ReadComponents(rootPath, agent, model);
                model.Agents.Add(agent);
            }
            catch (Exception ex)
            {
                model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                    $"Agent '{Path.GetFileName(directory)}' could not be read: {ex.Message}"));
            }
        }
    }

    private static AgentModel ReadBot(string botFile, string folderName)
    {
        var root = XDocument.Load(botFile).Root;
        var agent = new AgentModel
        {
            SchemaName = root?.Attribute("schemaname")?.Value ?? folderName,
            Name = root?.Element("name")?.Value ?? folderName,
            Language = root?.Element("language")?.Value ?? string.Empty,
            Template = root?.Element("template")?.Value ?? string.Empty,
            AuthenticationMode = root?.Element("authenticationmode")?.Value ?? string.Empty,
        };
        return agent;
    }

    private static void ReadConfiguration(string configurationFile, AgentModel agent)
    {
        if (!File.Exists(configurationFile)) return;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationFile));
            if (document.RootElement.TryGetProperty("settings", out var settings)
                && settings.TryGetProperty("GenerativeActionsEnabled", out var generative))
            {
                agent.GenerativeActionsEnabled = generative.ValueKind == JsonValueKind.True;
            }
        }
        catch
        {
            // Configuration is supplementary; a malformed file must not lose the agent.
        }
    }

    private static void ReadComponents(string rootPath, AgentModel agent, SolutionModel model)
    {
        var componentsDirectory = Path.Combine(rootPath, "botcomponents");
        if (!Directory.Exists(componentsDirectory)) return;

        foreach (var directory in Directory.EnumerateDirectories(componentsDirectory).OrderBy(d => d))
        {
            var folderName = Path.GetFileName(directory);
            // Components are prefixed with their owning agent's schema name.
            if (!folderName.StartsWith(agent.SchemaName + ".", StringComparison.OrdinalIgnoreCase))
                continue;

            var descriptor = Path.Combine(directory, "botcomponent.xml");
            var payload = Path.Combine(directory, "data");

            var name = folderName;
            var description = string.Empty;
            var parent = string.Empty;
            if (File.Exists(descriptor))
            {
                try
                {
                    var element = XDocument.Load(descriptor).Root;
                    name = element?.Element("name")?.Value ?? folderName;
                    description = element?.Element("description")?.Value ?? string.Empty;
                    parent = element?.Element("parentbotcomponentid")?.Element("schemaname")?.Value
                             ?? string.Empty;
                }
                catch
                {
                    // Fall back to the folder name.
                }
            }

            var yaml = File.Exists(payload) ? File.ReadAllText(payload) : string.Empty;
            var kind = Yaml.ScalarAt(yaml, "kind", 0) ?? string.Empty;

            switch (kind)
            {
                case "AdaptiveDialog":
                    agent.Topics.Add(ReadTopic(folderName, name, yaml));
                    break;

                case "AgentDialog":
                    // The agent component carries the agent's own instructions.
                    var instructions = Yaml.Block(yaml, "instructions");
                    if (!string.IsNullOrWhiteSpace(instructions) &&
                        string.IsNullOrWhiteSpace(agent.Instructions))
                    {
                        agent.Instructions = instructions.Trim();
                    }
                    agent.Tools.Add(new AgentToolModel
                    {
                        SchemaName = folderName, Name = name, Description = description,
                        Kind = kind, ParentComponent = parent,
                    });
                    break;

                case "TaskDialog":
                    agent.Tools.Add(new AgentToolModel
                    {
                        SchemaName = folderName, Name = name, Description = description,
                        Kind = kind, ParentComponent = parent,
                    });
                    break;

                case "GptComponentMetadata":
                    if (string.IsNullOrWhiteSpace(agent.Instructions))
                        agent.Instructions = (Yaml.Block(yaml, "instructions") ?? string.Empty).Trim();
                    break;

                case "KnowledgeSourceConfiguration":
                    agent.KnowledgeSources.Add(new AgentKnowledgeModel
                    {
                        SchemaName = folderName,
                        Name = name,
                        SourceKind = Yaml.ScalarAt(yaml, "kind", 1) ?? string.Empty,
                        Site = Yaml.Scalar(yaml, "site") ?? string.Empty,
                    });
                    break;

                case "ClosedListEntity":
                    break; // entities are structure, not behaviour

                default:
                    if (!string.IsNullOrEmpty(kind))
                    {
                        model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                            $"Agent component '{name}' has kind '{kind}', which Shieldsmith does not document yet."));
                    }
                    break;
            }
        }
    }

    private static AgentTopicModel ReadTopic(string schemaName, string name, string yaml)
    {
        var topic = new AgentTopicModel
        {
            SchemaName = schemaName,
            Name = name,
            DisplayName = Yaml.Scalar(yaml, "displayName") ?? name,
        };
        topic.TriggerQueries.AddRange(Yaml.Sequence(yaml, "triggerQueries"));
        // activity.text is a sequence of variation strings, not a scalar.
        topic.Messages.AddRange(Yaml.Sequence(yaml, "text"));
        topic.ActionKinds.AddRange(Yaml.AllScalars(yaml, "kind").Skip(1).Distinct());
        return topic;
    }
}

/// <summary>
/// A deliberately small YAML reader for agent payloads. These files are
/// machine-generated with a predictable shape, so targeted extraction is
/// enough and avoids a YAML dependency for a handful of fields.
/// </summary>
internal static class Yaml
{
    public static string? Scalar(string yaml, string key) => AllScalars(yaml, key).FirstOrDefault();

    public static string? ScalarAt(string yaml, string key, int index) =>
        AllScalars(yaml, key).Skip(index).FirstOrDefault();

    public static IEnumerable<string> AllScalars(string yaml, string key)
    {
        foreach (var line in Lines(yaml))
        {
            var trimmed = line.TrimStart();
            var prefix = key + ":";
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var value = trimmed[prefix.Length..].Trim();
            if (value.Length == 0 || value is "|" or "|-" or ">" or ">-") continue;
            yield return Unquote(value);
        }
    }

    /// <summary>Items of the first sequence under the given key.</summary>
    public static IEnumerable<string> Sequence(string yaml, string key)
    {
        var lines = Lines(yaml).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal)) continue;
            if (trimmed[(key.Length + 1)..].Trim().Length != 0) continue; // scalar, not a sequence

            var indent = Indent(lines[i]);
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].Trim().Length == 0) continue;
                var itemIndent = Indent(lines[j]);
                if (itemIndent <= indent) break;
                var item = lines[j].TrimStart();
                if (!item.StartsWith("- ", StringComparison.Ordinal)) break;
                yield return Unquote(item[2..].Trim());
            }
            break;
        }
    }

    /// <summary>Contents of a literal block scalar, for example "instructions: |-".</summary>
    public static string? Block(string yaml, string key)
    {
        var lines = Lines(yaml).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith(key + ":", StringComparison.Ordinal)) continue;

            var remainder = trimmed[(key.Length + 1)..].Trim();
            if (remainder.Length > 0 && remainder is not ("|" or "|-" or ">" or ">-"))
                return Unquote(remainder);

            var indent = Indent(lines[i]);
            var collected = new List<string>();
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].Trim().Length == 0) { collected.Add(string.Empty); continue; }
                if (Indent(lines[j]) <= indent) break;
                collected.Add(lines[j].TrimStart());
            }
            return string.Join("\n", collected).Trim();
        }
        return null;
    }

    private static IEnumerable<string> Lines(string yaml) =>
        yaml.Replace("\r\n", "\n").Split('\n');

    private static int Indent(string line) => line.Length - line.TrimStart().Length;

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            return value[1..^1];
        return value;
    }
}
