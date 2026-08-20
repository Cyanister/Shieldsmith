using System.Text;
using System.Text.RegularExpressions;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Outputs.Mermaid;

/// <summary>
/// Generates Mermaid source for a solution: an erDiagram for the data model and
/// a flowchart per cloud flow. Mermaid renders natively on GitHub and Azure
/// DevOps, so the Markdown output is a live diagram rather than a picture, and
/// <see cref="Rendering.MermaidRenderer"/> turns the same source into SVG/PNG.
/// </summary>
public static class MermaidGenerator
{
    /// <summary>Entity relationship diagram in Mermaid's erDiagram syntax.</summary>
    public static string BuildErd(SolutionModel model, bool includeColumns = true, int maxColumnsPerTable = 25)
    {
        var mermaid = new StringBuilder();
        mermaid.AppendLine("erDiagram");

        var drawable = model.Entities
            .Where(e => e.Attributes.Count > 0 || HasRelationship(model, e))
            .ToList();
        var names = drawable.ToDictionary(e => e.SchemaName, e => Identifier(e.SchemaName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var relationship in model.OneToManyRelationships)
        {
            var one = Resolve(drawable, relationship.ReferencedEntity);
            var many = Resolve(drawable, relationship.ReferencingEntity);
            if (one is null || many is null) continue;
            // ||--o{ : exactly one on the left, zero or more on the right.
            mermaid.AppendLine($"    {names[one.SchemaName]} ||--o{{ {names[many.SchemaName]} : " +
                               $"\"{Label(relationship.ReferencingAttribute)}\"");
        }

        foreach (var relationship in model.ManyToManyRelationships)
        {
            var first = Resolve(drawable, relationship.Entity1);
            var second = Resolve(drawable, relationship.Entity2);
            if (first is null || second is null) continue;
            mermaid.AppendLine($"    {names[first.SchemaName]} }}o--o{{ {names[second.SchemaName]} : " +
                               $"\"{Label(relationship.IntersectEntity)}\"");
        }

        if (includeColumns)
        {
            foreach (var entity in drawable.Where(e => e.Attributes.Count > 0))
            {
                mermaid.AppendLine($"    {names[entity.SchemaName]} {{");
                foreach (var attribute in entity.Attributes.Take(maxColumnsPerTable))
                {
                    var key = attribute.IsPrimaryId ? " PK" : string.Empty;
                    mermaid.AppendLine($"        {TypeToken(attribute)} {Identifier(attribute.LogicalName)}{key}");
                }
                if (entity.Attributes.Count > maxColumnsPerTable)
                {
                    mermaid.AppendLine($"        more {Identifier($"and_{entity.Attributes.Count - maxColumnsPerTable}_more")}");
                }
                mermaid.AppendLine("    }");
            }
        }

        return mermaid.ToString();
    }

    /// <summary>One cloud flow as a Mermaid flowchart.</summary>
    public static string BuildFlowchart(ProcessModel process, int maxNodes = 120)
    {
        var mermaid = new StringBuilder();
        mermaid.AppendLine("flowchart TD");

        var trigger = process.CloudFlow?.Trigger?.Summary ?? "Trigger";
        mermaid.AppendLine($"    trigger([\"{Label(trigger)}\"])");

        if (process.CloudFlow is null)
        {
            mermaid.AppendLine("    trigger --> none[\"No definition in this export\"]");
            return mermaid.ToString();
        }

        var counter = 0;
        var truncated = false;
        Emit(mermaid, process.CloudFlow.Actions, "trigger", ref counter, maxNodes, ref truncated);
        if (truncated)
            mermaid.AppendLine("    truncated[\"Diagram truncated; see the step table\"]");

        mermaid.AppendLine("    classDef trig fill:#E8F7F7,stroke:#00AAAA,stroke-width:2px;");
        mermaid.AppendLine("    classDef step fill:#F0FAFA,stroke:#00AAAA;");
        mermaid.AppendLine("    class trigger trig;");
        return mermaid.ToString();
    }

    private static string Emit(StringBuilder mermaid, IReadOnlyList<FlowAction> actions,
        string parentId, ref int counter, int maxNodes, ref bool truncated)
    {
        var previousId = parentId;
        foreach (var action in actions)
        {
            if (counter >= maxNodes) { truncated = true; return previousId; }

            var id = $"n{counter++}";
            var label = Label(action.DisplayName);
            var shape = action.Type.Equals("If", StringComparison.OrdinalIgnoreCase)
                        || action.Type.Equals("Switch", StringComparison.OrdinalIgnoreCase)
                ? $"{id}{{{{\"{label}\"}}}}"
                : $"{id}[\"{label}\"]";

            mermaid.AppendLine($"    {shape}");
            mermaid.AppendLine($"    {previousId} --> {id}");
            mermaid.AppendLine($"    class {id} step;");

            previousId = action.Children.Count > 0
                ? Emit(mermaid, action.Children, id, ref counter, maxNodes, ref truncated)
                : id;
        }
        return previousId;
    }

    /// <summary>Mermaid node ids must be identifier-safe.</summary>
    private static string Identifier(string value)
    {
        var cleaned = Regex.Replace(value, "[^A-Za-z0-9_]", "_");
        if (cleaned.Length == 0) cleaned = "unnamed";
        if (!char.IsLetter(cleaned[0]) && cleaned[0] != '_') cleaned = "t_" + cleaned;
        return cleaned;
    }

    /// <summary>Mermaid attribute types cannot contain spaces.</summary>
    private static string TypeToken(AttributeModel attribute)
    {
        var token = string.IsNullOrEmpty(attribute.Type) ? "field" : attribute.Type;
        return Regex.Replace(token, "[^A-Za-z0-9_]", "_");
    }

    /// <summary>Quoted-label text: strip quotes and newlines Mermaid cannot carry.</summary>
    private static string Label(string text)
    {
        if (string.IsNullOrEmpty(text)) return " ";
        var cleaned = text.Replace("\"", "'").Replace("\r", " ").Replace("\n", " ");
        return cleaned.Length > 70 ? cleaned[..67] + "..." : cleaned;
    }

    private static bool HasRelationship(SolutionModel model, EntityModel entity) =>
        model.OneToManyRelationships.Any(r =>
            Matches(r.ReferencedEntity, entity) || Matches(r.ReferencingEntity, entity)) ||
        model.ManyToManyRelationships.Any(r =>
            Matches(r.Entity1, entity) || Matches(r.Entity2, entity));

    private static EntityModel? Resolve(IEnumerable<EntityModel> entities, string name) =>
        entities.FirstOrDefault(e => Matches(name, e));

    private static bool Matches(string name, EntityModel entity) =>
        string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);
}
