using System.Text.Json;
using System.Text.Json.Serialization;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Export;

/// <summary>
/// The canonical JSON view of a parsed solution. Every downstream consumer that
/// needs structured data (AI payloads, the MCP server, the export pack) reads
/// this one shape so they can never drift apart.
/// </summary>
public static class SolutionJsonExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static string ToJson(SolutionModel model, bool redactEnvironmentValues = false)
    {
        var payload = new
        {
            solution = new
            {
                model.UniqueName,
                model.DisplayName,
                model.Version,
                model.IsManaged,
                publisher = new { model.PublisherUniqueName, model.PublisherDisplayName, model.CustomizationPrefix },
            },
            components = model.RootComponents.Select(c => new
            {
                c.TypeCode,
                c.TypeName,
                identifier = c.DisplayIdentifier,
            }),
            entities = model.Entities.Select(e => new
            {
                e.SchemaName,
                e.LogicalName,
                e.DisplayName,
                e.Description,
                e.OwnershipType,
                primaryId = e.PrimaryIdAttribute?.LogicalName,
                primaryName = e.PrimaryNameAttribute?.LogicalName,
                attributes = e.Attributes.Select(a => new
                {
                    a.LogicalName,
                    a.DisplayName,
                    type = a.TypeDisplay,
                    requiredLevel = a.RequiredLevel.Display(),
                    a.IsPrimaryId,
                    a.IsPrimaryName,
                    a.IsCustomField,
                }),
                alternateKeys = e.Keys.Select(k => new { k.LogicalName, k.DisplayName, k.KeyAttributes }),
                forms = e.Forms.Select(f => new
                {
                    f.Name,
                    f.FormType,
                    scriptLibraries = f.Libraries,
                    tabs = f.Tabs.Select(t => new
                    {
                        t.Label,
                        sections = t.Sections.Select(s => new { s.Label, s.Fields }),
                    }),
                }),
                views = e.Views.Select(v => new { v.Name, v.IsDefault, v.IsQuickFind, v.Columns }),
            }),
            optionSets = model.OptionSets.Select(o => new
            {
                o.Name,
                o.DisplayName,
                o.IsGlobal,
                options = o.Options.Select(v => new { v.Value, v.Label }),
            }),
            securityRoles = model.SecurityRoles.Select(r => new
            {
                r.Name,
                privilegeCount = r.Privileges.Count,
                byLevel = r.PrivilegeCountsByLevel,
            }),
            appModules = model.AppModules.Select(a => new { a.UniqueName, a.Name, a.ComponentCount }),
            siteMaps = model.SiteMaps.Select(s => new
            {
                s.UniqueName,
                areas = s.Areas.Select(area => new
                {
                    area.Title,
                    groups = area.Groups.Select(g => new
                    {
                        g.Title,
                        subAreas = g.SubAreas.Select(sa => new { sa.Title, sa.Entity }),
                    }),
                }),
            }),
            relationships = new
            {
                oneToMany = model.OneToManyRelationships.Select(r => new
                {
                    r.SchemaName,
                    one = r.ReferencedEntity,
                    many = r.ReferencingEntity,
                    lookupAttribute = r.ReferencingAttribute,
                    r.CascadeDelete,
                }),
                manyToMany = model.ManyToManyRelationships.Select(r => new
                {
                    r.SchemaName,
                    r.Entity1,
                    r.Entity2,
                    r.IntersectEntity,
                }),
                inferredLookups = model.InferredLookups.Select(r => new
                {
                    r.Entity,
                    attribute = r.AttributeLogicalName,
                    r.AttributeType,
                    inferred = true,
                }),
            },
            processes = model.Processes.Select(p => new
            {
                p.Name,
                kind = p.KindDisplay,
                p.PrimaryEntity,
                p.IsActive,
                cloudFlow = p.CloudFlow is null ? null : new
                {
                    trigger = p.CloudFlow.Trigger is null ? null : new
                    {
                        p.CloudFlow.Trigger.Summary,
                        p.CloudFlow.Trigger.Type,
                        p.CloudFlow.Trigger.Kind,
                        p.CloudFlow.Trigger.Connector,
                        p.CloudFlow.Trigger.EntityName,
                        p.CloudFlow.Trigger.Message,
                        p.CloudFlow.Trigger.Recurrence,
                    },
                    actions = p.CloudFlow.Actions.Select(SerializeAction),
                    connectors = p.CloudFlow.ConnectorsUsed,
                },
            }),
            canvasApps = model.CanvasApps.Select(a => new
            {
                a.Name,
                a.DisplayName,
                a.Description,
                kind = a.KindDisplay,
                a.AppVersion,
                a.FormFactor,
                a.HasInternals,
                dataSources = a.DataSources.Select(d => new { d.Name, d.LogicalName, d.EntitySetName }),
                globalVariables = a.GlobalVariables,
                collections = a.Collections,
                screens = a.Screens.Select(s => new
                {
                    s.Name,
                    s.Index,
                    controlCount = s.CountControls(),
                    controls = s.Controls.Select(SerializeControl),
                }),
            }),
            agents = model.Agents.Select(a => new
            {
                a.SchemaName,
                a.Name,
                authentication = a.AuthenticationModeDisplay,
                a.GenerativeActionsEnabled,
                a.Instructions,
                topics = a.Topics.Select(t => new
                {
                    t.Name, t.DisplayName, t.TriggerQueries, t.Messages, t.ActionKinds,
                }),
                tools = a.Tools.Select(t => new { t.Name, t.Description, t.Kind }),
                knowledgeSources = a.KnowledgeSources.Select(k => new { k.Name, k.SourceKind, k.Site }),
            }),
            pluginAssemblies = model.PluginAssemblies.Select(a => new
            {
                a.Name,
                a.FullName,
                a.Version,
                a.IsolationMode,
                types = a.Types.Select(t => new { name = t.DisplayName, t.AssemblyQualifiedName }),
                steps = a.Steps.Select(s => new
                {
                    s.Name, s.Message, s.PrimaryEntity, s.Stage, s.Mode, s.Rank,
                    filteringAttributes = s.FilteringAttributes, s.ImageCount,
                }),
            }),
            sdkMessageSteps = model.SdkMessageSteps.Select(s => new
            {
                s.Name, s.Message, s.PrimaryEntity, s.Stage, s.Mode, s.Rank,
                pluginType = s.PluginTypeName,
                filteringAttributes = s.FilteringAttributes,
            }),
            businessProcessFlows = model.Processes
                .Where(p => p.BusinessProcessFlow is not null)
                .Select(p => new
                {
                    p.Name,
                    p.PrimaryEntity,
                    p.IsActive,
                    stages = p.BusinessProcessFlow!.Stages.Select(s => new
                    {
                        s.Order,
                        s.Name,
                        steps = s.Steps.Select(step => new
                        {
                            step.Name, step.DataField, step.IsSystemControl,
                        }),
                    }),
                }),
            desktopFlows = model.Processes
                .Where(p => p.DesktopFlow is not null)
                .Select(p => new
                {
                    p.Name,
                    p.IsActive,
                    schemaVersion = p.DesktopFlow!.SchemaVersion,
                    modules = p.DesktopFlow.Modules,
                    inputs = p.DesktopFlow.Inputs.Select(v => new { v.Name, v.Type, v.Description }),
                    outputs = p.DesktopFlow.Outputs.Select(v => new { v.Name, v.Type }),
                    subflows = p.DesktopFlow.Subflows.Select(s => new
                    {
                        s.Name,
                        s.IsGlobal,
                        steps = s.Steps.Select(step => new
                        {
                            step.Action, step.Depth, step.IsDisabled, step.OutputVariables,
                        }),
                    }),
                }),
            connectionReferences = model.ConnectionReferences.Select(c => new
            {
                c.LogicalName,
                c.DisplayName,
                connector = c.ConnectorShortName,
            }),
            webResources = model.WebResources.Select(w => new
            {
                w.Name,
                w.DisplayName,
                type = w.TypeName,
            }),
            environmentVariables = model.EnvironmentVariables.Select(v => new
            {
                v.SchemaName,
                v.DisplayName,
                type = v.TypeName,
                v.IsRequired,
                v.IsSecret,
                defaultValue = Redact(v.DefaultValue, redactEnvironmentValues || v.IsSecret),
                currentValue = v.HasCurrentValue
                    ? Redact(v.CurrentValue, redactEnvironmentValues || v.IsSecret)
                    : null,
            }),
            diagnostics = model.Diagnostics.Select(d => d.ToString()),
        };

        return JsonSerializer.Serialize(payload, Options);
    }

    private static object SerializeControl(CanvasControlModel control) => new
    {
        control.Name,
        control.Type,
        properties = control.Properties,
        children = control.Children.Select(SerializeControl),
    };

    private static object SerializeAction(FlowAction action) => new
    {
        name = action.DisplayName,
        operation = action.OperationDisplay,
        children = action.Children.Select(SerializeAction),
    };

    private static string? Redact(string value, bool redact) =>
        string.IsNullOrEmpty(value) ? null : redact ? "[redacted]" : value;
}
