namespace Shieldsmith.Diagrams.Layout;

/// <summary>Measures text so node boxes are sized to their content.</summary>
public interface ITextMeasurer
{
    SizeD Measure(string text, double fontSize, bool bold);
}

public readonly record struct SizeD(double Width, double Height);

/// <summary>
/// Box sizing rules, shared by the layout engine and every renderer so a node's
/// measured size and its drawn size cannot drift apart.
/// </summary>
public static class NodeMeasurement
{
    public const double TitleFontSize = 13;
    public const double SubtitleFontSize = 9.5;
    public const double LineFontSize = 11;
    public const double PaddingX = 12;
    public const double PaddingY = 8;
    public const double LineSpacing = 4;
    /// <summary>Fixed gutter reserved for row markers such as "PK".</summary>
    public const double MarkerColumnWidth = 24;

    /// <summary>Height of the coloured title bar. Shared with the renderers.</summary>
    public static double HeaderHeight(Node node)
    {
        var height = PaddingY * 2 + TitleFontSize;
        if (!string.IsNullOrEmpty(node.Subtitle)) height += SubtitleFontSize + 2;
        return height;
    }

    public static void Measure(Node node, ITextMeasurer measurer)
    {
        if (node.IsDummy)
        {
            node.Width = 1;
            node.Height = 1;
            return;
        }

        // Width comes from real text measurement; height is derived from the
        // same constants the renderers advance by, so the box is exactly as
        // tall as the content drawn inside it. Mixing measured glyph heights
        // with fixed line advances leaves dead space at the bottom of a box.
        var width = measurer.Measure(node.Title, TitleFontSize, bold: true).Width;

        if (!string.IsNullOrEmpty(node.Subtitle))
            width = Math.Max(width, measurer.Measure(node.Subtitle, SubtitleFontSize, bold: false).Width);

        var markerWidth = node.Lines
            .Where(l => !string.IsNullOrEmpty(l.Marker))
            .Select(l => measurer.Measure(l.Marker!, LineFontSize, bold: true).Width)
            .DefaultIfEmpty(0)
            .Max();
        var markerOffset = markerWidth > 0 ? MarkerColumnWidth : 0;

        foreach (var line in node.Lines)
        {
            var lineWidth = measurer.Measure(line.Text, LineFontSize, line.Emphasise).Width + markerOffset;
            width = Math.Max(width, lineWidth);
        }

        var height = HeaderHeight(node);
        if (node.Lines.Count > 0)
            height += PaddingY + node.Lines.Count * (LineFontSize + LineSpacing);
        height += PaddingY;

        node.Width = Math.Max(width + PaddingX * 2, MinimumWidth(node.Shape));
        node.Height = Math.Max(height, MinimumHeight(node.Shape));

        if (node.Shape == NodeShape.Diamond)
        {
            // A diamond needs extra room: its text sits inside the inscribed box.
            node.Width *= 1.45;
            node.Height *= 1.6;
        }
    }

    private static double MinimumWidth(NodeShape shape) => shape switch
    {
        NodeShape.Stadium => 110,
        NodeShape.Diamond => 110,
        _ => 130,
    };

    private static double MinimumHeight(NodeShape shape) => shape switch
    {
        NodeShape.Record => 42,
        _ => 40,
    };
}
