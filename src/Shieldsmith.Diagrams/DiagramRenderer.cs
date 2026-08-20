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

        // The SVG is written first and is always exact. The raster is a
        // convenience for Word and the preview pane, so if GDI+ cannot produce
        // one the diagram is still delivered rather than the run being lost.
        var pngPath = Path.Combine(outputDirectory, baseName + ".png");
        string? warning = null;
        try
        {
            PngRenderer.Render(graph, size, pngPath, theme);
        }
        catch (Exception ex)
        {
            pngPath = null!;
            warning = $"'{baseName}' is {size.Width:0} by {size.Height:0} and could not be " +
                      $"rasterised ({ex.Message}); the SVG was written and is exact.";
        }

        return new DiagramFiles(svgPath, pngPath, size.Width, size.Height, warning);
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

public sealed record DiagramFiles(string SvgPath, string? PngPath, double Width, double Height,
    string? Warning = null);
