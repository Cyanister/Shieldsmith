namespace Shieldsmith.Core.Models;

public sealed class EnvironmentVariableModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TypeCode { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSecret { get; set; }
    public string DefaultValue { get; set; } = string.Empty;
    /// <summary>The environment-specific value, when the export carries one.</summary>
    public string CurrentValue { get; set; } = string.Empty;
    public bool HasCurrentValue { get; set; }

    public string TypeName => TypeCode switch
    {
        100000000 => "String",
        100000001 => "Number",
        100000002 => "Boolean",
        100000003 => "JSON",
        100000004 => "Data source",
        100000005 => "Secret",
        0 => "Unknown",
        _ => $"Type {TypeCode}",
    };
}
