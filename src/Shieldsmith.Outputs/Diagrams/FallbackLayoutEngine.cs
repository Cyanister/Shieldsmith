using Shieldsmith.Core.Models;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// A deliberately simple layered grid layout used when Graphviz is not on the
/// machine. Entities with the most relationships sit in the middle columns so
/// edge lines stay short. Good enough to read; installing Graphviz makes it
/// better, and the app says so rather than pretending.
/// </summary>
public static class FallbackLayoutEngine
{
    private const double CharWidthInches = 0.075;
    private const double LineHeightInches = 0.125;
    private const double TitleHeightInches = 0.3;
    private const double PaddingInches = 0.08;
    private const double GutterX = 0.9;
    private const double GutterY = 0.7;

    public static ErdLayout Layout(SolutionModel model, bool showAttributes)
    {
        var layout = new ErdLayout();

        var drawable = model.Entities
            .Where(e => e.Attributes.Count > 0 || Connectivity(model, e) > 0)
            .OrderByDescending(e => Connectivity(model, e))
            .ToList();
        if (drawable.Count == 0) return layout;

        foreach (var entity in drawable)
        {
            var node = new ErdNode
            {
                Id = entity.SchemaName,
                Title = string.IsNullOrEmpty(entity.DisplayName) ? entity.SchemaName : entity.DisplayName,
                Subtitle = entity.LogicalName,
            };
            if (showAttributes)
                node.Lines.AddRange(entity.Attributes.Select(DotBuilder.AttributeLine));

            var widestLine = node.Lines.Append(node.Title).Append(node.Subtitle).Max(l => l.Length);
            node.Width = Math.Clamp(widestLine * CharWidthInches + 2 * PaddingInches, 1.6, 4.5);
            node.Height = TitleHeightInches + LineHeightInches // title + subtitle
                          + node.Lines.Count * LineHeightInches + 2 * PaddingInches;
            layout.Nodes.Add(node);
        }

        // Grid: most-connected entities first, snaking so neighbours stay close.
        var columns = (int)Math.Ceiling(Math.Sqrt(layout.Nodes.Count));
        var rows = (int)Math.Ceiling(layout.Nodes.Count / (double)columns);
        var columnWidths = new double[columns];
        var rowHeights = new double[rows];

        for (var i = 0; i < layout.Nodes.Count; i++)
        {
            var (row, col) = Cell(i, columns);
            columnWidths[col] = Math.Max(columnWidths[col], layout.Nodes[i].Width);
            rowHeights[row] = Math.Max(rowHeights[row], layout.Nodes[i].Height);
        }

        var totalWidth = columnWidths.Sum() + (columns + 1) * GutterX;
        var totalHeight = rowHeights.Sum() + (rows + 1) * GutterY;
        layout.Width = totalWidth;
        layout.Height = totalHeight;

        for (var i = 0; i < layout.Nodes.Count; i++)
        {
            var (row, col) = Cell(i, columns);
            var x = GutterX + columnWidths.Take(col).Sum() + col * GutterX + columnWidths[col] / 2;
            // Origin bottom-left, rows fill from the top.
            var yTop = GutterY + rowHeights.Take(row).Sum() + row * GutterY + rowHeights[row] / 2;
            layout.Nodes[i].X = x;
            layout.Nodes[i].Y = totalHeight - yTop;
        }

        foreach (var rel in model.OneToManyRelationships)
        {
            if (layout.FindNode(rel.ReferencingEntity) is null || layout.FindNode(rel.ReferencedEntity) is null)
                continue;
            layout.Edges.Add(new ErdEdge
            {
                FromId = rel.ReferencingEntity,
                ToId = rel.ReferencedEntity,
                Label = rel.ReferencingAttribute,
            });
        }
        foreach (var rel in model.ManyToManyRelationships)
        {
            if (layout.FindNode(rel.Entity1) is null || layout.FindNode(rel.Entity2) is null)
                continue;
            layout.Edges.Add(new ErdEdge
            {
                FromId = rel.Entity1,
                ToId = rel.Entity2,
                Label = rel.IntersectEntity,
                Dashed = true,
            });
        }

        return layout;
    }

    private static (int Row, int Col) Cell(int index, int columns)
    {
        var row = index / columns;
        var col = index % columns;
        if (row % 2 == 1) col = columns - 1 - col; // snake
        return (row, col);
    }

    private static int Connectivity(SolutionModel model, EntityModel entity) =>
        model.OneToManyRelationships.Count(r =>
            Matches(r.ReferencingEntity, entity) || Matches(r.ReferencedEntity, entity)) +
        model.ManyToManyRelationships.Count(r =>
            Matches(r.Entity1, entity) || Matches(r.Entity2, entity));

    private static bool Matches(string name, EntityModel entity) =>
        string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);
}
