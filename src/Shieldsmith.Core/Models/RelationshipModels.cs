namespace Shieldsmith.Core.Models;

/// <summary>
/// A one-to-many relationship as declared in EntityRelationships. The referenced
/// entity is the "one" side; the referencing entity holds the lookup and is the
/// "many" side.
/// </summary>
public sealed class OneToManyRelationship
{
    public string SchemaName { get; set; } = string.Empty;
    public string ReferencedEntity { get; set; } = string.Empty;
    public string ReferencingEntity { get; set; } = string.Empty;
    public string ReferencingAttribute { get; set; } = string.Empty;
    public string CascadeDelete { get; set; } = string.Empty;
    public bool IsHierarchical { get; set; }
}

public sealed class ManyToManyRelationship
{
    public string SchemaName { get; set; } = string.Empty;
    public string Entity1 { get; set; } = string.Empty;
    public string Entity2 { get; set; } = string.Empty;
    public string IntersectEntity { get; set; } = string.Empty;
}

/// <summary>
/// A relationship that exists only because an attribute is lookup-shaped (owner,
/// customer, party list, or a lookup with no declared relationship in this
/// solution). Inferred, not declared: always labelled as such in output.
/// </summary>
public sealed class InferredLookupRelationship
{
    public string Entity { get; set; } = string.Empty;
    public string AttributeLogicalName { get; set; } = string.Empty;
    public string AttributeDisplayName { get; set; } = string.Empty;
    public string AttributeType { get; set; } = string.Empty;
}
