namespace Shieldsmith.Core.Models;

/// <summary>
/// The parsed picture of one solution export. Everything downstream (documents,
/// diagrams, JSON export, AI payloads) reads from this model and nothing else.
/// </summary>
public sealed class SolutionModel
{
    public string UniqueName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsManaged { get; set; }
    public string PublisherUniqueName { get; set; } = string.Empty;
    public string PublisherDisplayName { get; set; } = string.Empty;
    public string CustomizationPrefix { get; set; } = string.Empty;

    public List<RootComponent> RootComponents { get; } = new();
    public List<EntityModel> Entities { get; } = new();
    public List<OneToManyRelationship> OneToManyRelationships { get; } = new();
    public List<ManyToManyRelationship> ManyToManyRelationships { get; } = new();
    public List<InferredLookupRelationship> InferredLookups { get; } = new();
    public List<EnvironmentVariableModel> EnvironmentVariables { get; } = new();
    public List<ProcessModel> Processes { get; } = new();
    public List<ConnectionReferenceModel> ConnectionReferences { get; } = new();
    public List<WebResourceModel> WebResources { get; } = new();
    public List<CanvasAppModel> CanvasApps { get; } = new();
    public List<AgentModel> Agents { get; } = new();
    public List<OptionSetModel> OptionSets { get; } = new();
    public List<SecurityRoleModel> SecurityRoles { get; } = new();
    public List<AppModuleModel> AppModules { get; } = new();
    public List<SiteMapModel> SiteMaps { get; } = new();

    /// <summary>Non-fatal observations made while parsing: unresolved references,
    /// components that could not be fully read. Surfaced, never hidden.</summary>
    public List<Diagnostic> Diagnostics { get; } = new();

    public EntityModel? FindEntity(string schemaOrLogicalName) =>
        Entities.FirstOrDefault(e =>
            string.Equals(e.SchemaName, schemaOrLogicalName, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(e.LogicalName, schemaOrLogicalName, StringComparison.OrdinalIgnoreCase));

    public string EntityDisplayName(string schemaOrLogicalName)
    {
        var entity = FindEntity(schemaOrLogicalName);
        return entity is null || string.IsNullOrEmpty(entity.DisplayName)
            ? schemaOrLogicalName
            : entity.DisplayName;
    }
}

public sealed class RootComponent
{
    public int TypeCode { get; set; }
    public string TypeName { get; set; } = string.Empty;
    /// <summary>Present for schema-named components (entities, app modules, option sets).</summary>
    public string SchemaName { get; set; } = string.Empty;
    /// <summary>Present for GUID-identified components (workflows, forms, canvas apps).</summary>
    public string Id { get; set; } = string.Empty;
    public string Behavior { get; set; } = string.Empty;
    /// <summary>Name resolved from elsewhere in the export when the manifest only carries a GUID.</summary>
    public string ResolvedName { get; set; } = string.Empty;

    /// <summary>Best available identifier: schema name, then a resolved name, then the GUID.</summary>
    public string DisplayIdentifier =>
        !string.IsNullOrEmpty(SchemaName) ? SchemaName
        : !string.IsNullOrEmpty(ResolvedName) ? ResolvedName
        : Id;
}

public sealed class Diagnostic
{
    public DiagnosticSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;

    public Diagnostic() { }
    public Diagnostic(DiagnosticSeverity severity, string message)
    {
        Severity = severity;
        Message = message;
    }

    public override string ToString() => $"{Severity}: {Message}";
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
}
