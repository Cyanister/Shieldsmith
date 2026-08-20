namespace Shieldsmith.Core.Models;

/// <summary>
/// A plugin assembly in the solution, the types it contains, and the steps that
/// register those types against a table and a message. Read from
/// customizations.xml; see PluginParser.
/// </summary>
public sealed class PluginAssemblyModel
{
    /// <summary>Short name, e.g. "Contoso.Travel.Plugins".</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>The full strong name including version, culture and public key token.</summary>
    public string FullName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    /// <summary>Sandbox or full trust. Full trust is worth noticing in a review.</summary>
    public string IsolationMode { get; set; } = string.Empty;
    /// <summary>Path to the .dll inside the export.</summary>
    public string FileName { get; set; } = string.Empty;

    public List<PluginTypeModel> Types { get; } = new();
    /// <summary>Steps whose plugin type was matched to this assembly.</summary>
    public List<SdkMessageStepModel> Steps { get; } = new();
}

public sealed class PluginTypeModel
{
    public string Name { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string AssemblyQualifiedName { get; set; } = string.Empty;

    public string DisplayName => FriendlyName.Length > 0 ? FriendlyName : Name;
}

/// <summary>
/// One registration: this plugin type runs on this message, against this table,
/// at this pipeline stage.
/// </summary>
public sealed class SdkMessageStepModel
{
    public string Name { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string PluginTypeName { get; set; } = string.Empty;
    public string PrimaryEntity { get; set; } = string.Empty;
    /// <summary>
    /// Create, Update, Delete and so on. Empty when the export does not settle
    /// it: the step carries only a message GUID, and the name it was read from
    /// had been changed. Never guessed.
    /// </summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Pre-validation, pre-operation or post-operation.</summary>
    public string Stage { get; set; } = string.Empty;
    /// <summary>Synchronous or asynchronous.</summary>
    public string Mode { get; set; } = string.Empty;
    /// <summary>Execution order within the stage.</summary>
    public int Rank { get; set; }
    /// <summary>Columns that trigger the step; empty means every column.</summary>
    public List<string> FilteringAttributes { get; } = new();
    public int ImageCount { get; set; }

    /// <summary>"Update of ct_trip, pre-operation, synchronous" for a table of steps.</summary>
    public string Summary
    {
        get
        {
            var message = Message.Length > 0 ? Message : "an unnamed message";
            var target = PrimaryEntity.Length > 0 ? $" of {PrimaryEntity}" : string.Empty;
            var parts = new List<string> { message + target };
            if (Stage.Length > 0) parts.Add(Stage.ToLowerInvariant());
            if (Mode.Length > 0) parts.Add(Mode.ToLowerInvariant());
            return string.Join(", ", parts);
        }
    }
}
