using System.ComponentModel;
using System.Collections.Concurrent;
using System.Text.Json;
using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Shieldsmith.Mcp;

/// <summary>
/// The MCP tool surface over parsed solutions. Solutions are parsed on demand
/// from their export zips and held in memory keyed by unique name. Results are
/// size-capped so a large solution cannot flood the client's context.
/// </summary>
[McpServerToolType]
public static class SolutionTools
{
    private static readonly ConcurrentDictionary<string, SolutionModel> Solutions =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const int MaxListItems = 200;

    [McpServerTool, Description(
        "Load a Power Platform solution export zip so its contents can be queried. " +
        "Returns a summary. Must be called before the other tools for that solution.")]
    public static string load_solution(
        [Description("Absolute path to the solution export .zip file")] string zipPath)
    {
        using var unpacked = SolutionUnpacker.Unpack(zipPath);
        var model = SolutionParser.Parse(unpacked);
        Solutions[model.UniqueName] = model;
        return Serialize(new
        {
            loaded = model.UniqueName,
            model.DisplayName,
            model.Version,
            model.IsManaged,
            publisher = model.PublisherDisplayName,
            tables = model.Entities.Count,
            processes = model.Processes.Count,
            relationships = model.OneToManyRelationships.Count + model.ManyToManyRelationships.Count,
            environmentVariables = model.EnvironmentVariables.Count,
            canvasApps = model.CanvasApps.Count,
            agents = model.Agents.Count,
            hint = "Use get_solution_summary, list_components, get_entity, get_flow, get_canvas_app, " +
                   "get_agent, get_relationships or search next.",
        });
    }

    [McpServerTool, Description("List the solutions currently loaded in this server.")]
    public static string list_loaded_solutions() =>
        Serialize(Solutions.Values.Select(s => new { s.UniqueName, s.DisplayName, s.Version }));

    [McpServerTool, Description("Overview of a loaded solution: counts, tables, processes, apps.")]
    public static string get_solution_summary(
        [Description("Solution unique name from load_solution")] string solutionName)
    {
        var model = Find(solutionName);
        return Serialize(new
        {
            model.UniqueName,
            model.DisplayName,
            model.Version,
            model.IsManaged,
            publisher = model.PublisherDisplayName,
            tables = model.Entities.Select(e => new { e.DisplayName, e.LogicalName, columns = e.Attributes.Count }),
            processes = model.Processes.Select(p => new { p.Name, kind = p.KindDisplay, p.PrimaryEntity, p.IsActive }),
            apps = model.AppModules.Select(a => new { a.Name, a.UniqueName }),
            canvasApps = model.CanvasApps.Select(a => new
            {
                name = a.DisplayName.Length > 0 ? a.DisplayName : a.Name,
                kind = a.KindDisplay,
                screens = a.HasInternals ? a.Screens.Count : (int?)null,
                internalsRead = a.HasInternals,
            }),
            agents = model.Agents.Select(a => new
            {
                name = a.Name.Length > 0 ? a.Name : a.SchemaName,
                topics = a.Topics.Count,
                tools = a.Tools.Count,
                knowledgeSources = a.KnowledgeSources.Count,
            }),
            securityRoles = model.SecurityRoles.Select(r => r.Name),
            environmentVariables = model.EnvironmentVariables.Select(v => new { v.SchemaName, type = v.TypeName }),
            diagnostics = model.Diagnostics.Select(d => d.ToString()),
        });
    }

    [McpServerTool, Description("List a loaded solution's root components, optionally filtered by type name substring.")]
    public static string list_components(
        [Description("Solution unique name")] string solutionName,
        [Description("Optional filter, e.g. 'flow', 'table', 'web resource'")] string? typeFilter = null)
    {
        var model = Find(solutionName);
        var components = model.RootComponents
            .Where(c => typeFilter is null || c.TypeName.Contains(typeFilter, StringComparison.OrdinalIgnoreCase))
            .Select(c => new { c.TypeName, identifier = c.DisplayIdentifier });
        return SerializeCapped(components, MaxListItems);
    }

    [McpServerTool, Description(
        "Full detail for one table: columns with types and requirement levels, keys, forms, views, " +
        "and the relationships that touch it.")]
    public static string get_entity(
        [Description("Solution unique name")] string solutionName,
        [Description("Table logical or schema name, e.g. ct_trip")] string entityName)
    {
        var model = Find(solutionName);
        var entity = model.FindEntity(entityName)
            ?? throw new McpException($"No table named '{entityName}' in {model.UniqueName}. " +
                                      "Use get_solution_summary to list tables.");
        return Serialize(new
        {
            entity.SchemaName,
            entity.LogicalName,
            entity.DisplayName,
            entity.Description,
            entity.OwnershipType,
            columns = entity.Attributes.Select(a => new
            {
                a.LogicalName,
                a.DisplayName,
                type = a.TypeDisplay,
                requirement = a.RequiredLevel.Display(),
                a.IsPrimaryId,
                a.IsPrimaryName,
            }),
            alternateKeys = entity.Keys.Select(k => new { k.LogicalName, k.KeyAttributes }),
            forms = entity.Forms.Select(f => new
            {
                f.Name,
                f.FormType,
                tabs = f.Tabs.Select(t => new { t.Label, sections = t.Sections.Select(s => new { s.Label, s.Fields }) }),
                f.Libraries,
            }),
            views = entity.Views.Select(v => new { v.Name, v.IsDefault, v.Columns }),
            relationships = RelationshipsFor(model, entity),
        });
    }

    [McpServerTool, Description(
        "Relationships in a loaded solution: one-to-many, many-to-many and inferred lookups. " +
        "Optionally scoped to one table.")]
    public static string get_relationships(
        [Description("Solution unique name")] string solutionName,
        [Description("Optional table logical or schema name to scope to")] string? entityName = null)
    {
        var model = Find(solutionName);
        var entity = entityName is null ? null : model.FindEntity(entityName);
        if (entityName is not null && entity is null)
            throw new McpException($"No table named '{entityName}' in {model.UniqueName}.");

        if (entity is not null) return Serialize(RelationshipsFor(model, entity));
        return Serialize(new
        {
            oneToMany = model.OneToManyRelationships.Select(r => new
            {
                r.SchemaName, one = r.ReferencedEntity, many = r.ReferencingEntity,
                lookup = r.ReferencingAttribute, r.CascadeDelete,
            }),
            manyToMany = model.ManyToManyRelationships.Select(r => new
            {
                r.SchemaName, r.Entity1, r.Entity2, r.IntersectEntity,
            }),
            inferredLookups = model.InferredLookups.Select(l => new
            {
                l.Entity, l.AttributeLogicalName, l.AttributeType, inferred = true,
            }),
        });
    }

    [McpServerTool, Description("Full detail for one process: trigger, action tree and connectors for a cloud flow.")]
    public static string get_flow(
        [Description("Solution unique name")] string solutionName,
        [Description("Process name as shown in get_solution_summary")] string flowName)
    {
        var model = Find(solutionName);
        var process = model.Processes.FirstOrDefault(p =>
                          string.Equals(p.Name, flowName, StringComparison.OrdinalIgnoreCase))
                      ?? throw new McpException($"No process named '{flowName}' in {model.UniqueName}.");
        return Serialize(new
        {
            process.Name,
            kind = process.KindDisplay,
            process.PrimaryEntity,
            process.IsActive,
            trigger = process.CloudFlow?.Trigger is { } trigger ? new
            {
                trigger.Summary, trigger.Type, trigger.Kind, trigger.Connector,
                trigger.EntityName, trigger.Message, trigger.Recurrence,
            } : null,
            actions = process.CloudFlow is null ? null : SerializeActions(process.CloudFlow.Actions),
            connectors = process.CloudFlow?.ConnectorsUsed,
            // A desktop flow is a Robin script, not an action tree, so it has
            // its own shape: named subflows of ordered, nested action steps.
            desktopFlow = process.DesktopFlow is not { } desktop ? null : new
            {
                desktop.SchemaVersion,
                desktop.Modules,
                inputs = desktop.Inputs.Select(v => new { v.Name, v.Type, v.Description }),
                outputs = desktop.Outputs.Select(v => new { v.Name, v.Type, v.Description }),
                subflows = desktop.Subflows.Select(s => new
                {
                    s.Name,
                    s.IsGlobal,
                    stepCount = s.Steps.Count,
                    steps = s.Steps.Take(MaxListItems).Select(step => new
                    {
                        step.Action, step.Module, step.Depth, step.IsDisabled, step.OutputVariables,
                    }),
                    truncated = s.Steps.Count > MaxListItems,
                }),
            },
        });
    }

    [McpServerTool, Description(
        "A canvas app or component library. Without screenName: app metadata, data sources, " +
        "variables and the screen list. With screenName: that screen's control tree and the " +
        "formulas the maker authored on it.")]
    public static string get_canvas_app(
        [Description("Solution unique name")] string solutionName,
        [Description("Canvas app display name or schema name")] string appName,
        [Description("Optional screen name to drill into; omit for the app overview")] string? screenName = null)
    {
        var model = Find(solutionName);
        var app = model.CanvasApps.FirstOrDefault(a =>
                      string.Equals(a.DisplayName, appName, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(a.Name, appName, StringComparison.OrdinalIgnoreCase))
                  ?? throw new McpException($"No canvas app named '{appName}' in {model.UniqueName}. " +
                                            "Use get_solution_summary to list canvas apps.");

        if (screenName is not null)
        {
            var screen = app.Screens.FirstOrDefault(s =>
                             string.Equals(s.Name, screenName, StringComparison.OrdinalIgnoreCase))
                         ?? throw new McpException($"No screen named '{screenName}' in '{appName}'. " +
                                                   $"Screens: {string.Join(", ", app.Screens.Select(s => s.Name))}.");
            return Serialize(new
            {
                app = app.DisplayName,
                screen = screen.Name,
                screen.Index,
                controls = SerializeControls(screen.Controls),
            });
        }

        return Serialize(new
        {
            app.Name,
            app.DisplayName,
            app.Description,
            kind = app.KindDisplay,
            app.FormFactor,
            app.DocumentVersion,
            app.LastSavedUtc,
            // False means the export carried metadata but no .msapp to read;
            // it is not a parse failure and must not be reported as one.
            internalsRead = app.HasInternals,
            dataSources = app.DataSources.Select(d => new { d.Name, d.LogicalName, d.EntitySetName }),
            globalVariables = app.GlobalVariables,
            collections = app.Collections,
            controlCounts = app.ControlCounts,
            screens = app.Screens.Select(s => new
            {
                s.Name,
                s.Index,
                controls = s.AllControls().Count(),
            }),
            hint = app.HasInternals && app.Screens.Count > 0
                ? "Call again with screenName to see a screen's control tree and formulas."
                : null,
        });
    }

    [McpServerTool, Description(
        "A Copilot Studio agent: its instructions, topics with trigger phrases and message " +
        "variations, tools, and knowledge sources.")]
    public static string get_agent(
        [Description("Solution unique name")] string solutionName,
        [Description("Agent name or schema name")] string agentName)
    {
        var model = Find(solutionName);
        var agent = model.Agents.FirstOrDefault(a =>
                        string.Equals(a.Name, agentName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a.SchemaName, agentName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new McpException($"No agent named '{agentName}' in {model.UniqueName}. " +
                                              "Use get_solution_summary to list agents.");
        return Serialize(new
        {
            agent.SchemaName,
            agent.Name,
            agent.Language,
            agent.Template,
            authentication = agent.AuthenticationModeDisplay,
            agent.GenerativeActionsEnabled,
            agent.Instructions,
            topics = agent.Topics.Select(t => new
            {
                t.DisplayName, t.SchemaName, t.TriggerQueries, t.Messages, t.ActionKinds,
            }),
            tools = agent.Tools.Select(t => new { t.Name, t.Kind, t.Description, t.ParentComponent }),
            knowledgeSources = agent.KnowledgeSources.Select(k => new { k.Name, k.SourceKind, k.Site }),
        });
    }

    [McpServerTool, Description("One choice (option set) with its values and labels.")]
    public static string get_option_set(
        [Description("Solution unique name")] string solutionName,
        [Description("Option set name, e.g. ct_bookingtype")] string optionSetName)
    {
        var model = Find(solutionName);
        var optionSet = model.OptionSets.FirstOrDefault(o =>
                            string.Equals(o.Name, optionSetName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new McpException($"No option set named '{optionSetName}' in {model.UniqueName}.");
        return Serialize(new
        {
            optionSet.Name, optionSet.DisplayName, optionSet.IsGlobal,
            options = optionSet.Options.Select(o => new { o.Value, o.Label }),
        });
    }

    [McpServerTool, Description("One security role with its privilege list.")]
    public static string get_security_role(
        [Description("Solution unique name")] string solutionName,
        [Description("Role name")] string roleName)
    {
        var model = Find(solutionName);
        var role = model.SecurityRoles.FirstOrDefault(r =>
                       string.Equals(r.Name, roleName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new McpException($"No security role named '{roleName}' in {model.UniqueName}.");
        return Serialize(new
        {
            role.Name,
            privilegeCount = role.Privileges.Count,
            byLevel = role.PrivilegeCountsByLevel,
            privileges = role.Privileges.Take(MaxListItems).Select(p => new { p.Name, p.Level }),
            truncated = role.Privileges.Count > MaxListItems,
        });
    }

    [McpServerTool, Description(
        "Search a loaded solution by name: tables, columns, processes, web resources, " +
        "environment variables and roles whose names or display names contain the text.")]
    public static string search(
        [Description("Solution unique name")] string solutionName,
        [Description("Text to search for")] string text)
    {
        var model = Find(solutionName);
        bool Hits(params string[] values) =>
            values.Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase));

        var results = new
        {
            tables = model.Entities.Where(e => Hits(e.LogicalName, e.DisplayName))
                .Select(e => new { e.LogicalName, e.DisplayName }).Take(MaxListItems),
            columns = model.Entities.SelectMany(e => e.Attributes
                    .Where(a => Hits(a.LogicalName, a.DisplayName))
                    .Select(a => new { table = e.LogicalName, a.LogicalName, a.DisplayName }))
                .Take(MaxListItems),
            processes = model.Processes.Where(p => Hits(p.Name))
                .Select(p => new { p.Name, kind = p.KindDisplay }).Take(MaxListItems),
            flowActions = model.Processes
                .Where(p => p.CloudFlow is not null)
                .SelectMany(p => p.CloudFlow!.AllActions()
                    .Where(a => Hits(a.Name, a.OperationId))
                    .Select(a => new { flow = p.Name, action = a.DisplayName, a.OperationId }))
                .Take(MaxListItems),
            webResources = model.WebResources.Where(w => Hits(w.Name, w.DisplayName))
                .Select(w => new { w.Name, type = w.TypeName }).Take(MaxListItems),
            environmentVariables = model.EnvironmentVariables.Where(v => Hits(v.SchemaName, v.DisplayName))
                .Select(v => new { v.SchemaName, type = v.TypeName }).Take(MaxListItems),
            roles = model.SecurityRoles.Where(r => Hits(r.Name)).Select(r => r.Name).Take(MaxListItems),
            canvasApps = model.CanvasApps.Where(a => Hits(a.Name, a.DisplayName))
                .Select(a => new { a.Name, a.DisplayName, kind = a.KindDisplay }).Take(MaxListItems),
            canvasControls = model.CanvasApps.SelectMany(a => a.Screens
                    .SelectMany(s => s.AllControls()
                        .Where(c => Hits(c.Name, c.Type))
                        .Select(c => new { app = a.DisplayName, screen = s.Name, c.Name, c.Type })))
                .Take(MaxListItems),
            agents = model.Agents.Where(a => Hits(a.Name, a.SchemaName))
                .Select(a => new { a.Name, a.SchemaName }).Take(MaxListItems),
            agentTopics = model.Agents.SelectMany(a => a.Topics
                    .Where(t => Hits(t.DisplayName, t.SchemaName))
                    .Select(t => new { agent = a.Name, t.DisplayName, triggers = t.TriggerQueries.Count }))
                .Take(MaxListItems),
            desktopFlowSteps = model.Processes
                .Where(p => p.DesktopFlow is not null)
                .SelectMany(p => p.DesktopFlow!.Subflows
                    .SelectMany(s => s.Steps
                        .Where(step => Hits(step.Action, step.Module))
                        .Select(step => new { flow = p.Name, subflow = s.Name, step.Action })))
                .Take(MaxListItems),
        };
        return Serialize(results);
    }

    private static SolutionModel Find(string solutionName) =>
        Solutions.TryGetValue(solutionName, out var model)
            ? model
            : throw new McpException(
                $"Solution '{solutionName}' is not loaded. Call load_solution with its zip path first." +
                (Solutions.IsEmpty ? "" : $" Loaded: {string.Join(", ", Solutions.Keys)}."));

    private static object RelationshipsFor(SolutionModel model, EntityModel entity)
    {
        bool Matches(string name) =>
            string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);

        return new
        {
            oneToMany = model.OneToManyRelationships
                .Where(r => Matches(r.ReferencedEntity) || Matches(r.ReferencingEntity))
                .Select(r => new { r.SchemaName, one = r.ReferencedEntity, many = r.ReferencingEntity, lookup = r.ReferencingAttribute }),
            manyToMany = model.ManyToManyRelationships
                .Where(r => Matches(r.Entity1) || Matches(r.Entity2))
                .Select(r => new { r.SchemaName, r.Entity1, r.Entity2, r.IntersectEntity }),
            inferredLookups = model.InferredLookups
                .Where(l => Matches(l.Entity))
                .Select(l => new { l.AttributeLogicalName, l.AttributeType, inferred = true }),
        };
    }

    private static object SerializeControls(IEnumerable<CanvasControlModel> controls) =>
        controls.Select(c => new
        {
            c.Name,
            c.Type,
            // Only maker-authored formulas are kept by the parser, so an empty
            // properties map means the maker wrote nothing, not that it failed.
            properties = c.Properties,
            children = SerializeControls(c.Children),
        });

    private static object SerializeActions(IEnumerable<FlowAction> actions) =>
        actions.Select(a => new
        {
            name = a.DisplayName,
            operation = a.OperationDisplay,
            children = SerializeActions(a.Children),
        });

    // McpException comes from the SDK: its message is surfaced to the client as
    // a tool error the model can read and correct, unlike arbitrary exceptions.

    private static string Serialize(object value) => JsonSerializer.Serialize(value, Json);

    private static string SerializeCapped<T>(IEnumerable<T> items, int cap)
    {
        var list = items.Take(cap + 1).ToList();
        var truncated = list.Count > cap;
        if (truncated) list.RemoveAt(list.Count - 1);
        return Serialize(new { items = list, truncated });
    }
}
