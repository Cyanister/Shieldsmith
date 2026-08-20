using System.Text.Json;
using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads the Workflows block of customizations.xml and, for cloud flows, the
/// clientdata JSON file the export carries alongside it: trigger, the action
/// tree with nesting, and connection references. Unknown action shapes are kept
/// generically rather than dropped.
/// </summary>
public static class FlowParser
{
    public static void Parse(XDocument customizations, string rootPath, SolutionModel model)
    {
        foreach (var reference in customizations.Root?.Element("connectionreferences")
                     ?.Elements("connectionreference") ?? [])
        {
            model.ConnectionReferences.Add(new ConnectionReferenceModel
            {
                LogicalName = reference.Attribute("connectionreferencelogicalname")?.Value ?? string.Empty,
                DisplayName = reference.Element("connectionreferencedisplayname")?.Value ?? string.Empty,
                ConnectorId = reference.Element("connectorid")?.Value ?? string.Empty,
            });
        }

        foreach (var workflow in customizations.Root?.Element("Workflows")?.Elements("Workflow") ?? [])
        {
            var process = new ProcessModel
            {
                Id = workflow.Attribute("WorkflowId")?.Value.Trim('{', '}').ToLowerInvariant() ?? string.Empty,
                Name = workflow.Attribute("Name")?.Value ?? string.Empty,
                Category = int.TryParse(workflow.Element("Category")?.Value, out var category) ? category : -1,
                PrimaryEntity = workflow.Element("PrimaryEntity")?.Value ?? string.Empty,
                IsActive = workflow.Element("StateCode")?.Value.Trim() == "1",
                FileName = workflow.Element("JsonFileName")?.Value
                           ?? workflow.Element("XamlFileName")?.Value
                           ?? string.Empty,
            };

            if (process.Kind == ProcessKind.DesktopFlow)
            {
                DesktopFlowParser.Attach(workflow, process, rootPath, model);
            }
            else if (process.Kind == ProcessKind.CloudFlow && !string.IsNullOrEmpty(process.FileName))
            {
                var jsonPath = Path.Combine(rootPath,
                    process.FileName.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        process.CloudFlow = ParseClientData(File.ReadAllText(jsonPath));
                    }
                    catch (Exception ex)
                    {
                        model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                            $"Cloud flow '{process.Name}' has a definition file that could not be parsed: {ex.Message}"));
                    }
                }
                else
                {
                    model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                        $"Cloud flow '{process.Name}' references definition file '{process.FileName}' which is not in the export."));
                }
            }

            model.Processes.Add(process);
        }
    }

    public static CloudFlowDetail ParseClientData(string clientDataJson)
    {
        var detail = new CloudFlowDetail();
        using var document = JsonDocument.Parse(clientDataJson);
        if (!document.RootElement.TryGetProperty("properties", out var properties))
            return detail;

        if (properties.TryGetProperty("connectionReferences", out var references)
            && references.ValueKind == JsonValueKind.Object)
        {
            foreach (var reference in references.EnumerateObject())
            {
                detail.ConnectionReferences.Add(new FlowConnectionReference
                {
                    ApiName = GetString(reference.Value, "api", "name")
                              ?? reference.Name,
                    LogicalName = GetString(reference.Value, "connection", "connectionReferenceLogicalName")
                                  ?? string.Empty,
                });
            }
        }

        if (!properties.TryGetProperty("definition", out var definition))
            return detail;

        if (definition.TryGetProperty("triggers", out var triggers)
            && triggers.ValueKind == JsonValueKind.Object)
        {
            foreach (var trigger in triggers.EnumerateObject().Take(1))
                detail.Trigger = ParseTrigger(trigger.Name, trigger.Value);
        }

        if (definition.TryGetProperty("actions", out var actions))
            detail.Actions.AddRange(ParseActions(actions));

        return detail;
    }

    private static FlowTrigger ParseTrigger(string name, JsonElement element)
    {
        var trigger = new FlowTrigger
        {
            Name = name,
            Type = GetString(element, "type") ?? string.Empty,
            Kind = GetString(element, "kind") ?? string.Empty,
            OperationId = GetString(element, "inputs", "host", "operationId") ?? string.Empty,
            Connector = ConnectorFromApiId(GetString(element, "inputs", "host", "apiId")),
        };

        if (element.TryGetProperty("inputs", out var inputs)
            && inputs.TryGetProperty("parameters", out var parameters)
            && parameters.ValueKind == JsonValueKind.Object)
        {
            foreach (var parameter in parameters.EnumerateObject())
            {
                if (parameter.Name.EndsWith("/entityname", StringComparison.OrdinalIgnoreCase))
                    trigger.EntityName = parameter.Value.ToString();
                else if (parameter.Name.EndsWith("/message", StringComparison.OrdinalIgnoreCase))
                    trigger.Message = DecodeDataverseMessage(parameter.Value.ToString());
            }
        }

        if (element.TryGetProperty("recurrence", out var recurrence))
        {
            var interval = GetString(recurrence, "interval");
            var frequency = GetString(recurrence, "frequency");
            if (interval is not null || frequency is not null)
                trigger.Recurrence = $"every {interval ?? "?"} {frequency?.ToLowerInvariant() ?? "?"}";
        }

        return trigger;
    }

    private static List<FlowAction> ParseActions(JsonElement actionsElement)
    {
        var actions = new List<FlowAction>();
        if (actionsElement.ValueKind != JsonValueKind.Object) return actions;

        foreach (var property in actionsElement.EnumerateObject())
        {
            var element = property.Value;
            var action = new FlowAction
            {
                Name = property.Name,
                Type = GetString(element, "type") ?? string.Empty,
                OperationId = GetString(element, "inputs", "host", "operationId") ?? string.Empty,
                Connector = ConnectorFromApiId(GetString(element, "inputs", "host", "apiId")),
            };

            if (element.TryGetProperty("runAfter", out var runAfter)
                && runAfter.ValueKind == JsonValueKind.Object)
            {
                foreach (var dependency in runAfter.EnumerateObject())
                    action.RunAfter.Add(dependency.Name);
            }

            // Nested actions: Scope/Foreach/Until carry "actions"; If adds "else";
            // Switch carries "cases" (each with "actions") and "default".
            if (element.TryGetProperty("actions", out var nested))
                action.Children.AddRange(ParseActions(nested));
            if (element.TryGetProperty("else", out var elseBranch)
                && elseBranch.TryGetProperty("actions", out var elseActions))
                action.Children.AddRange(ParseActions(elseActions));
            if (element.TryGetProperty("cases", out var cases)
                && cases.ValueKind == JsonValueKind.Object)
            {
                foreach (var caseProperty in cases.EnumerateObject())
                    if (caseProperty.Value.TryGetProperty("actions", out var caseActions))
                        action.Children.AddRange(ParseActions(caseActions));
            }
            if (element.TryGetProperty("default", out var defaultCase)
                && defaultCase.TryGetProperty("actions", out var defaultActions))
                action.Children.AddRange(ParseActions(defaultActions));

            actions.Add(action);
        }

        // Order siblings so dependencies come before dependants, keeping document
        // order for ties. runAfter cycles cannot occur in a valid definition.
        return OrderByRunAfter(actions);
    }

    private static List<FlowAction> OrderByRunAfter(List<FlowAction> actions)
    {
        var ordered = new List<FlowAction>(actions.Count);
        var remaining = new List<FlowAction>(actions);
        var placedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var localNames = new HashSet<string>(actions.Select(a => a.Name), StringComparer.OrdinalIgnoreCase);

        while (remaining.Count > 0)
        {
            var next = remaining.FirstOrDefault(a =>
                a.RunAfter.All(dependency => !localNames.Contains(dependency) || placedNames.Contains(dependency)));
            if (next is null)
            {
                // Unresolvable ordering: keep document order for what is left.
                ordered.AddRange(remaining);
                break;
            }
            ordered.Add(next);
            placedNames.Add(next.Name);
            remaining.Remove(next);
        }
        return ordered;
    }

    private static string DecodeDataverseMessage(string code) => code switch
    {
        "1" => "created",
        "2" => "deleted",
        "3" => "updated",
        "4" => "created or updated",
        "5" => "created or deleted",
        "6" => "updated or deleted",
        "7" => "created, updated or deleted",
        _ => code,
    };

    private static string ConnectorFromApiId(string? apiId)
    {
        if (string.IsNullOrEmpty(apiId)) return string.Empty;
        var index = apiId.LastIndexOf('/');
        return index >= 0 ? apiId[(index + 1)..] : apiId;
    }

    private static string? GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out current))
                return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }
}
