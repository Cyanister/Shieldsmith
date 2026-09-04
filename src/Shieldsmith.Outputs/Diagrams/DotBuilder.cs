using System.Text;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// Builds the Graphviz DOT description of the solution's data model. Labels are
/// escaped, node ids are the entity schema names, and inferred relationships are
/// drawn dashed so declared fact and inference never look the same.
/// </summary>
public static class DotBuilder
{
    public static string Build(SolutionModel model, bool showAttributes = true, bool showRelationships = true)
    {
        var dot = new StringBuilder();
        dot.AppendLine("digraph ERD {");
        dot.AppendLine("  rankdir=TB;");
        dot.AppendLine("  splines=ortho;");
        dot.AppendLine("  nodesep=0.6; ranksep=0.8;");
        // Brand palette, matching DiagramTheme: teal on light teal tint.
        dot.AppendLine("  node [shape=record, style=filled, fillcolor=\"#F0FAFA\", color=\"#00AAAA\", fontcolor=\"#0F172A\", fontname=\"Segoe UI\", fontsize=10];");
        dot.AppendLine("  edge [fontname=\"Segoe UI\", fontsize=9, color=\"#64748B\"];");
        dot.AppendLine();

        var drawable = model.Entities.Where(e => e.Attributes.Count > 0 || HasAnyRelationship(model, e)).ToList();

        foreach (var entity in drawable)
        {
            var title = string.IsNullOrEmpty(entity.DisplayName) ? entity.SchemaName : entity.DisplayName;
            if (showAttributes && entity.Attributes.Count > 0)
            {
                var lines = entity.Attributes.Select(AttributeLine);
                dot.AppendLine($"  {NodeId(entity.SchemaName)} [label=\"{{{Escape(title)}|{string.Join("\\l", lines.Select(Escape))}\\l}}\"];");
            }
            else
            {
                dot.AppendLine($"  {NodeId(entity.SchemaName)} [label=\"{Escape(title)}\"];");
            }
        }

        if (showRelationships)
        {
            dot.AppendLine();
            foreach (var rel in model.OneToManyRelationships)
            {
                if (!InDiagram(drawable, rel.ReferencingEntity) || !InDiagram(drawable, rel.ReferencedEntity))
                    continue;
                // Many side points at the one side, crow's foot on the many end.
                dot.AppendLine($"  {NodeId(rel.ReferencingEntity)} -> {NodeId(rel.ReferencedEntity)} " +
                               $"[label=\"{Escape(rel.ReferencingAttribute)}\", arrowhead=none, arrowtail=crow, dir=both];");
            }

            foreach (var rel in model.ManyToManyRelationships)
            {
                if (!InDiagram(drawable, rel.Entity1) || !InDiagram(drawable, rel.Entity2))
                    continue;
                dot.AppendLine($"  {NodeId(rel.Entity1)} -> {NodeId(rel.Entity2)} " +
                               $"[label=\"{Escape(rel.IntersectEntity)}\", arrowhead=crow, arrowtail=crow, dir=both, style=dashed];");
            }
        }

        dot.AppendLine("}");
        return dot.ToString();
    }

    public static string AttributeLine(AttributeModel attribute)
    {
        var marker = attribute.IsPrimaryId ? "PK " : attribute.IsPrimaryName ? "N " : string.Empty;
        var required = attribute.IsRequired ? " *" : string.Empty;
        var name = string.IsNullOrEmpty(attribute.DisplayName) ? attribute.LogicalName : attribute.DisplayName;
        return $"{marker}{name}: {attribute.TypeDisplay}{required}";
    }

    private static bool HasAnyRelationship(SolutionModel model, EntityModel entity) =>
        model.OneToManyRelationships.Any(r =>
            SameEntity(r.ReferencingEntity, entity) || SameEntity(r.ReferencedEntity, entity)) ||
        model.ManyToManyRelationships.Any(r =>
            SameEntity(r.Entity1, entity) || SameEntity(r.Entity2, entity));

    private static bool SameEntity(string name, EntityModel entity) =>
        string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);

    private static bool InDiagram(IEnumerable<EntityModel> drawable, string name) =>
        drawable.Any(e => SameEntity(name, e));

    /// <summary>Node id: quoted schema name, safe for any characters.</summary>
    internal static string NodeId(string schemaName) => $"\"{schemaName.Replace("\"", "\\\"")}\"";

    /// <summary>Escapes text for use inside a DOT record label.</summary>
    internal static string Escape(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '{': builder.Append("\\{"); break;
                case '}': builder.Append("\\}"); break;
                case '|': builder.Append("\\|"); break;
                case '<': builder.Append("\\<"); break;
                case '>': builder.Append("\\>"); break;
                case '\n':
                case '\r': builder.Append(' '); break;
                default: builder.Append(c); break;
            }
        }
        return builder.ToString();
    }
}
