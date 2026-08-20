using Shieldsmith.Diagrams.Layout;
using Shieldsmith.Diagrams.Rendering;

namespace Shieldsmith.Diagrams;

/// <summary>
/// The one call site for Shieldsmith's built-in graphing: lay a graph out and write
/// it as SVG and PNG. No Graphviz, no Node, no network.
/// </summary>
public static class DiagramRenderer
{
    public static DiagramFiles Render(Graph graph, string outputDirectory, string baseName,
        DiagramTheme? theme = null, LayeredLayout.Options? layoutOptions = null)
    {
        theme ??= DiagramTheme.Brand;
        Directory.CreateDirectory(outputDirectory);

        using var measurer = new GdiTextMeasurer(theme.RenderFontFamily);
        var size = new LayeredLayout(layoutOptions).Apply(graph, measurer);

        var svgPath = Path.Combine(outputDirectory, baseName + ".svg");
        File.WriteAllText(svgPath, SvgRenderer.Render(graph, size, theme));

        var pngPath = Path.Combine(outputDirectory, baseName + ".png");
        PngRenderer.Render(graph, size, pngPath, theme);

        return new DiagramFiles(svgPath, pngPath, size.Width, size.Height);
    }

    /// <summary>Lays out without writing files, for callers that only need coordinates.</summary>
    public static LayoutResult Layout(Graph graph, DiagramTheme? theme = null,
        LayeredLayout.Options? layoutOptions = null)
    {
        theme ??= DiagramTheme.Brand;
        using var measurer = new GdiTextMeasurer(theme.RenderFontFamily);
        return new LayeredLayout(layoutOptions).Apply(graph, measurer);
    }
}

public sealed record DiagramFiles(string SvgPath, string PngPath, double Width, double Height);
