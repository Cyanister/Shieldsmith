namespace Shieldsmith.Core.Models;

public sealed class EntityModel
{
    /// <summary>Schema-cased name as it appears in the export, e.g. "valto_Admissions".</summary>
    public string SchemaName { get; set; } = string.Empty;
    /// <summary>Lowercase logical name, e.g. "valto_admissions".</summary>
    public string LogicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EntitySetName { get; set; } = string.Empty;
    public string OwnershipType { get; set; } = string.Empty;
    public bool IsAuditEnabled { get; set; }
    public bool IsActivity { get; set; }

    public List<AttributeModel> Attributes { get; } = new();
    public List<EntityKeyModel> Keys { get; } = new();
    public List<FormModel> Forms { get; } = new();
    public List<ViewModel> Views { get; } = new();

    public AttributeModel? PrimaryIdAttribute => Attributes.FirstOrDefault(a => a.IsPrimaryId);
    public AttributeModel? PrimaryNameAttribute => Attributes.FirstOrDefault(a => a.IsPrimaryName);
}

public sealed class AttributeModel
{
    /// <summary>Schema-cased physical name, e.g. "valto_CamSisID".</summary>
    public string PhysicalName { get; set; } = string.Empty;
    /// <summary>Lowercase logical name, e.g. "valto_camsisid".</summary>
    public string LogicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Raw type token from the export, e.g. "nvarchar", "lookup", "picklist".</summary>
    public string Type { get; set; } = string.Empty;
    public RequiredLevel RequiredLevel { get; set; } = RequiredLevel.None;
    public bool IsPrimaryId { get; set; }
    public bool IsPrimaryName { get; set; }
    public bool IsCustomField { get; set; }

    public bool IsRequired => RequiredLevel is RequiredLevel.ApplicationRequired or RequiredLevel.SystemRequired;

    public bool IsLookupStyle => Type.ToLowerInvariant() is "lookup" or "owner" or "customer" or "partylist";

    /// <summary>Friendly label for the raw export type token.</summary>
    public string TypeDisplay => Type.ToLowerInvariant() switch
    {
        "nvarchar" => "Text",
        "ntext" => "Multiline text",
        "int" => "Whole number",
        "decimal" => "Decimal",
        "float" => "Float",
        "money" => "Currency",
        "bit" => "Yes/No",
        "datetime" => "Date and time",
        "picklist" => "Choice",
        "multiselectpicklist" => "Choices (multi-select)",
        "lookup" => "Lookup",
        "owner" => "Owner",
        "customer" => "Customer",
        "partylist" => "Party list",
        "state" => "Status",
        "status" => "Status reason",
        "primarykey" => "Unique identifier (primary key)",
        "uniqueidentifier" => "Unique identifier",
        "file" => "File",
        "image" => "Image",
        _ => Type,
    };
}

public sealed class EntityKeyModel
{
    public string LogicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> KeyAttributes { get; } = new();
}

public enum RequiredLevel
{
    None,
    Recommended,
    ApplicationRequired,
    SystemRequired,
}

public static class RequiredLevelParser
{
    /// <summary>Parses the export token: none, recommended, required, systemrequired.</summary>
    public static RequiredLevel Parse(string? token) => token?.Trim().ToLowerInvariant() switch
    {
        "required" => RequiredLevel.ApplicationRequired,
        "applicationrequired" => RequiredLevel.ApplicationRequired,
        "systemrequired" => RequiredLevel.SystemRequired,
        "recommended" => RequiredLevel.Recommended,
        _ => RequiredLevel.None,
    };

    public static string Display(this RequiredLevel level) => level switch
    {
        RequiredLevel.SystemRequired => "System required",
        RequiredLevel.ApplicationRequired => "Required",
        RequiredLevel.Recommended => "Recommended",
        _ => "Optional",
    };
}
