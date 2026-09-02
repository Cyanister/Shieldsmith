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
        Emit(mermaid, process.CloudFlow.Actions, new[] { "trigger" }, ref counter, maxNodes, ref truncated);
        if (truncated)
            mermaid.AppendLine("    truncated[\"Diagram truncated; see the step table\"]");

        mermaid.AppendLine("    classDef trig fill:#E8F7F7,stroke:#00AAAA,stroke-width:2px;");
        mermaid.AppendLine("    classDef step fill:#F0FAFA,stroke:#00AAAA;");
        mermaid.AppendLine("    class trigger trig;");
        return mermaid.ToString();
    }

    /// <summary>
    /// Emits one list of sibling actions and returns the ids anything following
    /// them should attach to. Mirrors the built-in engine: order comes from
    /// runAfter so parallel actions stay parallel, and a container becomes a
    /// Mermaid subgraph so its contents are boxed rather than inlined.
    /// </summary>
    private static List<string> Emit(StringBuilder mermaid, IReadOnlyList<FlowAction> actions,
        IReadOnlyList<string> entryIds, ref int counter, int maxNodes, ref bool truncated)
    {
        if (actions.Count == 0) return entryIds.ToList();

        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exitsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var previousExits = entryIds.ToList();

        foreach (var action in actions)
        {
            if (counter >= maxNodes) { truncated = true; break; }

            var id = $"n{counter++}";
            idByName[action.Name] = id;

            var label = Label(action.DisplayName);
            var isBranching = action.Type.Equals("If", StringComparison.OrdinalIgnoreCase)
                              || action.Type.Equals("Switch", StringComparison.OrdinalIgnoreCase);
            mermaid.AppendLine(isBranching
                ? $"    {id}{{{{\"{label}\"}}}}"
                : $"    {id}[\"{label}\"]");
            mermaid.AppendLine($"    class {id} step;");

            var predecessors = new List<string>();
            foreach (var name in action.RunAfter)
                if (exitsByName.TryGetValue(name, out var exits)) predecessors.AddRange(exits);
                else if (idByName.TryGetValue(name, out var sibling)) predecessors.Add(sibling);
            // With no usable runAfter, follow the previous sibling; only the
            // first action in a list starts from the sequence entry.
            if (predecessors.Count == 0) predecessors.AddRange(previousExits);

            foreach (var from in predecessors.Distinct())
                mermaid.AppendLine($"    {from} --> {id}");

            exitsByName[action.Name] = action.Branches.Count > 0
                ? EmitContainer(mermaid, action, id, ref counter, maxNodes, ref truncated)
                : new List<string> { id };
            previousExits = exitsByName[action.Name];
        }

        var consumed = actions.SelectMany(a => a.RunAfter)
            .Where(idByName.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaves = actions
            .Where(a => idByName.ContainsKey(a.Name) && !consumed.Contains(a.Name))
            .SelectMany(a => exitsByName[a.Name])
            .Distinct()
            .ToList();
        return leaves.Count > 0 ? leaves : entryIds.ToList();
    }

    private static List<string> EmitContainer(StringBuilder mermaid, FlowAction action,
        string headerId, ref int counter, int maxNodes, ref bool truncated)
    {
        var exits = new List<string>();
        foreach (var branch in action.Branches)
        {
            if (counter >= maxNodes) { truncated = true; break; }

            // A subgraph is Mermaid's box-around-a-group. The title is what
            // makes a Catch scope legible as a Catch scope.
            var groupId = $"g{counter++}";
            var title = branch.Label.Length > 0
                ? $"{Label(action.DisplayName)}: {Label(branch.Label)}"
                : Label(action.DisplayName);
            mermaid.AppendLine($"    subgraph {groupId} [\"{title}\"]");

            var branchExits = Emit(mermaid, branch.Actions, new[] { headerId },
                ref counter, maxNodes, ref truncated);

            mermaid.AppendLine("    end");
            exits.AddRange(branchExits);
        }

        return exits.Count > 0 ? exits : new List<string> { headerId };
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
