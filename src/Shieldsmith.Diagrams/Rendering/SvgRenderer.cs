using System.Globalization;
using System.Text;
using Shieldsmith.Diagrams.Layout;

namespace Shieldsmith.Diagrams.Rendering;

/// <summary>
/// Draws a laid-out graph as SVG: resolution independent, small, and readable
/// in any browser or Word. This is Shieldsmith's primary diagram format.
///
/// Every number goes through <see cref="N"/> so output is byte-identical
/// regardless of the machine's locale: a comma decimal separator would produce
/// silently corrupt SVG.
/// </summary>
public static class SvgRenderer
{
    public static string Render(Graph graph, LayoutResult size, DiagramTheme? theme = null)
    {
        theme ??= DiagramTheme.Brand;
        var svg = new StringBuilder();
        var width = N(Math.Ceiling(size.Width));
        var height = N(Math.Ceiling(size.Height));

        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" " +
                       $"viewBox=\"0 0 {width} {height}\" font-family=\"{Escape(theme.FontFamily)}\">");

        svg.AppendLine("  <defs>");
        svg.AppendLine($"    <marker id=\"arrow\" viewBox=\"0 0 10 10\" refX=\"9\" refY=\"5\" " +
                       $"markerWidth=\"7\" markerHeight=\"7\" orient=\"auto-start-reverse\">" +
                       $"<path d=\"M 0 0 L 10 5 L 0 10 z\" fill=\"{theme.EdgeColour}\"/></marker>");
        svg.AppendLine($"    <marker id=\"crow\" viewBox=\"0 0 10 10\" refX=\"1\" refY=\"5\" " +
                       $"markerWidth=\"9\" markerHeight=\"9\" orient=\"auto-start-reverse\">" +
                       $"<path d=\"M 10 0 L 0 5 L 10 10\" fill=\"none\" stroke=\"{theme.EdgeColour}\" " +
                       $"stroke-width=\"1.4\"/></marker>");
        svg.AppendLine("    <filter id=\"cardShadow\" x=\"-20%\" y=\"-20%\" width=\"140%\" height=\"140%\">");
        svg.AppendLine("      <feDropShadow dx=\"0\" dy=\"1\" stdDeviation=\"1.5\" flood-opacity=\"0.10\"/>");
        svg.AppendLine("    </filter>");
        svg.AppendLine("  </defs>");

        svg.AppendLine($"  <rect width=\"{width}\" height=\"{height}\" fill=\"{theme.Canvas}\"/>");

        // Edges, then nodes, then edge labels: labels drawn before the nodes
        // would be hidden underneath them.
        foreach (var edge in graph.Edges) RenderEdge(svg, edge, theme);
        foreach (var node in graph.Nodes.Where(n => !n.IsDummy)) RenderNode(svg, node, theme);
        foreach (var edge in graph.Edges) RenderEdgeLabel(svg, edge, theme);

        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    private static void RenderEdge(StringBuilder svg, Edge edge, DiagramTheme theme)
    {
        if (edge.Waypoints.Count < 2) return;

        var path = new StringBuilder();
        path.Append($"M {N(edge.Waypoints[0].X)} {N(edge.Waypoints[0].Y)}");
        for (var i = 1; i < edge.Waypoints.Count; i++)
        {
            var point = edge.Waypoints[i];
            if (i < edge.Waypoints.Count - 1)
            {
                // Curve through the dummy node rather than turning a hard corner.
                var next = edge.Waypoints[i + 1];
                var midX = (point.X + next.X) / 2;
                var midY = (point.Y + next.Y) / 2;
                path.Append($" Q {N(point.X)} {N(point.Y)} {N(midX)} {N(midY)}");
            }
            else
            {
                path.Append($" L {N(point.X)} {N(point.Y)}");
            }
        }

        var dash = edge.Dashed ? " stroke-dasharray=\"6 4\"" : string.Empty;
        var markerStart = edge.FromEnding switch
        {
            EdgeEnding.Arrow => " marker-start=\"url(#arrow)\"",
            EdgeEnding.CrowsFoot => " marker-start=\"url(#crow)\"",
            _ => string.Empty,
        };
        var markerEnd = edge.ToEnding switch
        {
            EdgeEnding.Arrow => " marker-end=\"url(#arrow)\"",
            EdgeEnding.CrowsFoot => " marker-end=\"url(#crow)\"",
            _ => string.Empty,
        };

        svg.AppendLine($"  <path d=\"{path}\" fill=\"none\" stroke=\"{theme.EdgeColour}\" " +
                       $"stroke-width=\"1.4\"{dash}{markerStart}{markerEnd}/>");
    }

    private static void RenderEdgeLabel(StringBuilder svg, Edge edge, DiagramTheme theme)
    {
        if (string.IsNullOrEmpty(edge.Label) || edge.Waypoints.Count < 2) return;

        var mid = Midpoint(edge);
        var labelWidth = edge.Label.Length * 5.6 + 10;
        svg.AppendLine($"  <rect x=\"{N(mid.X - labelWidth / 2)}\" y=\"{N(mid.Y - 9)}\" " +
                       $"width=\"{N(labelWidth)}\" height=\"16\" rx=\"4\" " +
                       $"fill=\"{theme.Surface}\" fill-opacity=\"0.94\"/>");
        svg.AppendLine($"  <text x=\"{N(mid.X)}\" y=\"{N(mid.Y + 3)}\" font-size=\"9.5\" " +
                       $"fill=\"{theme.Muted}\" text-anchor=\"middle\">{Escape(edge.Label)}</text>");
    }

    /// <summary>
    /// The point half way along the polyline by arc length. Indexing the
    /// waypoint list instead puts a two-point edge's label on its endpoint,
    /// which lands on top of the target node.
    /// </summary>
    public static PointD Midpoint(Edge edge)
    {
        var total = 0.0;
        for (var i = 1; i < edge.Waypoints.Count; i++)
            total += Distance(edge.Waypoints[i - 1], edge.Waypoints[i]);

        var target = total / 2;
        var travelled = 0.0;
        for (var i = 1; i < edge.Waypoints.Count; i++)
        {
            var segment = Distance(edge.Waypoints[i - 1], edge.Waypoints[i]);
            if (travelled + segment >= target && segment > 0)
            {
                var t = (target - travelled) / segment;
                var a = edge.Waypoints[i - 1];
                var b = edge.Waypoints[i];
                return new PointD(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
            }
            travelled += segment;
        }
        return edge.Waypoints[^1];
    }

    private static double Distance(PointD a, PointD b) =>
        Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));

    private static void RenderNode(StringBuilder svg, Node node, DiagramTheme theme)
    {
        var accent = node.AccentColour ?? theme.Primary;

        switch (node.Shape)
        {
            case NodeShape.Diamond:
                RenderDiamond(svg, node, theme, accent);
                return;
            case NodeShape.Stadium:
                RenderSimpleBox(svg, node, theme, accent, node.Height / 2);
                return;
            case NodeShape.RoundedBox:
                RenderSimpleBox(svg, node, theme, accent, theme.CornerRadius);
                return;
        }

        // Record: a tinted header bar with the body rows beneath it.
        var radius = theme.CornerRadius;
        var headerHeight = NodeMeasurement.HeaderHeight(node);

        svg.AppendLine("  <g filter=\"url(#cardShadow)\">");
        svg.AppendLine($"    <rect x=\"{N(node.Left)}\" y=\"{N(node.Top)}\" " +
                       $"width=\"{N(node.Width)}\" height=\"{N(node.Height)}\" rx=\"{N(radius)}\" " +
                       $"fill=\"{theme.Surface}\" stroke=\"{theme.Border}\" stroke-width=\"1\"/>");
        svg.AppendLine("  </g>");

        // Header with square bottom corners so it meets the body cleanly.
        svg.AppendLine($"  <path d=\"M {N(node.Left)} {N(node.Top + radius)} " +
                       $"a {N(radius)} {N(radius)} 0 0 1 {N(radius)} {N(-radius)} " +
                       $"h {N(node.Width - radius * 2)} " +
                       $"a {N(radius)} {N(radius)} 0 0 1 {N(radius)} {N(radius)} " +
                       $"v {N(headerHeight - radius)} h {N(-node.Width)} z\" fill=\"{accent}\"/>");

        var textX = node.Left + NodeMeasurement.PaddingX;
        var y = node.Top + NodeMeasurement.PaddingY + NodeMeasurement.TitleFontSize;
        svg.AppendLine($"  <text x=\"{N(textX)}\" y=\"{N(y)}\" " +
                       $"font-size=\"{N(NodeMeasurement.TitleFontSize)}\" font-weight=\"600\" " +
                       $"fill=\"#FFFFFF\">{Escape(node.Title)}</text>");

        if (!string.IsNullOrEmpty(node.Subtitle))
        {
            y += NodeMeasurement.SubtitleFontSize + 3;
            svg.AppendLine($"  <text x=\"{N(textX)}\" y=\"{N(y)}\" " +
                           $"font-size=\"{N(NodeMeasurement.SubtitleFontSize)}\" " +
                           $"fill=\"#FFFFFF\" fill-opacity=\"0.82\">{Escape(node.Subtitle)}</text>");
        }

        var hasMarkers = node.Lines.Any(l => !string.IsNullOrEmpty(l.Marker));
        y = node.Top + headerHeight + NodeMeasurement.PaddingY + NodeMeasurement.LineFontSize * 0.85;
        foreach (var line in node.Lines)
        {
            if (!string.IsNullOrEmpty(line.Marker))
            {
                svg.AppendLine($"  <text x=\"{N(textX)}\" y=\"{N(y)}\" " +
                               $"font-size=\"{N(NodeMeasurement.LineFontSize - 1.5)}\" font-weight=\"700\" " +
                               $"fill=\"{theme.PrimaryDark}\">{Escape(line.Marker)}</text>");
            }
            var weight = line.Emphasise ? " font-weight=\"600\"" : string.Empty;
            svg.AppendLine($"  <text x=\"{N(textX + (hasMarkers ? NodeMeasurement.MarkerColumnWidth : 0))}\" y=\"{N(y)}\" " +
                           $"font-size=\"{N(NodeMeasurement.LineFontSize)}\"{weight} " +
                           $"fill=\"{theme.Text}\">{Escape(line.Text)}</text>");
            y += NodeMeasurement.LineFontSize + NodeMeasurement.LineSpacing;
        }
    }

    private static void RenderSimpleBox(StringBuilder svg, Node node, DiagramTheme theme,
        string accent, double radius)
    {
        svg.AppendLine("  <g filter=\"url(#cardShadow)\">");
        svg.AppendLine($"    <rect x=\"{N(node.Left)}\" y=\"{N(node.Top)}\" " +
                       $"width=\"{N(node.Width)}\" height=\"{N(node.Height)}\" rx=\"{N(radius)}\" " +
                       $"fill=\"{theme.TintLight}\" stroke=\"{accent}\" stroke-width=\"1.5\"/>");
        svg.AppendLine("  </g>");
        RenderCentredLabel(svg, node, theme);
    }

    private static void RenderDiamond(StringBuilder svg, Node node, DiagramTheme theme, string accent)
    {
        var points = $"{N(node.X)},{N(node.Top)} {N(node.Right)},{N(node.Y)} " +
                     $"{N(node.X)},{N(node.Bottom)} {N(node.Left)},{N(node.Y)}";
        svg.AppendLine($"  <polygon points=\"{points}\" fill=\"{theme.TintMid}\" " +
                       $"stroke=\"{accent}\" stroke-width=\"1.5\"/>");
        RenderCentredLabel(svg, node, theme);
    }

    private static void RenderCentredLabel(StringBuilder svg, Node node, DiagramTheme theme)
    {
        var hasSubtitle = !string.IsNullOrEmpty(node.Subtitle);
        var y = hasSubtitle ? node.Y - 1 : node.Y + NodeMeasurement.TitleFontSize * 0.35;
        svg.AppendLine($"  <text x=\"{N(node.X)}\" y=\"{N(y)}\" " +
                       $"font-size=\"{N(NodeMeasurement.TitleFontSize - 1)}\" font-weight=\"600\" " +
                       $"fill=\"{theme.Text}\" text-anchor=\"middle\">{Escape(node.Title)}</text>");
        if (hasSubtitle)
        {
            svg.AppendLine($"  <text x=\"{N(node.X)}\" y=\"{N(y + 13)}\" " +
                           $"font-size=\"{N(NodeMeasurement.SubtitleFontSize)}\" " +
                           $"fill=\"{theme.Muted}\" text-anchor=\"middle\">{Escape(node.Subtitle!)}</text>");
        }
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
