using Shieldsmith.Core.Models;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// The one entry point for producing the ERD. Prefers Graphviz for layout and
/// rendering; falls back to the built-in layout and renderer when it is absent,
/// and says which happened instead of failing quietly.
/// </summary>
public static class ErdGenerator
{
    public static ErdResult Generate(
        SolutionModel model,
        string outputDirectory,
        string baseName,
        bool showAttributes = true,
        GraphvizRunner? runner = null)
    {
        Directory.CreateDirectory(outputDirectory);
        runner ??= new GraphvizRunner();

        var result = new ErdResult
        {
            DotPath = Path.Combine(outputDirectory, baseName + ".dot"),
        };

        // A solution with no tables is perfectly normal: a Copilot Studio agent,
        // a flow-only solution, a web resource pack. There is simply no data
        // model to draw. Returning an empty result says so; throwing here used
        // to abort the whole analysis and report an agent solution as empty.
        if (model.Entities.Count == 0)
        {
            result.Warning = "This solution contains no tables, so there is no data model to draw.";
            return result;
        }

        var dot = DotBuilder.Build(model, showAttributes);
        File.WriteAllText(result.DotPath, dot);

        if (runner.IsAvailable)
        {
            try
            {
                var pngPath = Path.Combine(outputDirectory, baseName + ".png");
                var svgPath = Path.Combine(outputDirectory, baseName + ".svg");
                runner.Render(result.DotPath, pngPath, "png");
                runner.Render(result.DotPath, svgPath, "svg");
                result.PngPath = pngPath;
                result.SvgPath = svgPath;
                result.Layout = DotPlainLayoutReader.Parse(runner.RenderPlain(result.DotPath));
                result.UsedGraphviz = true;
                return result;
            }
            catch (GraphvizException ex)
            {
                result.Warning = $"Graphviz failed ({ex.Message}); used the built-in layout instead.";
            }
        }
        else
        {
            result.Warning = "Graphviz is not installed; used the built-in layout. " +
                             "To improve the Visio layout, install it with " +
                             "'winget install Graphviz.Graphviz' (or from graphviz.org); " +
                             "no PATH change is needed, Shieldsmith finds it in Program Files.";
        }

        result.Layout = FallbackLayoutEngine.Layout(model, showAttributes);
        var fallbackPng = Path.Combine(outputDirectory, baseName + ".png");
        FallbackErdRenderer.RenderPng(result.Layout, fallbackPng);
        result.PngPath = fallbackPng;
        result.UsedGraphviz = false;
        return result;
    }
}

public sealed class ErdResult
{
    public string DotPath { get; set; } = string.Empty;
    public string? PngPath { get; set; }
    public string? SvgPath { get; set; }
    public bool UsedGraphviz { get; set; }
    public ErdLayout Layout { get; set; } = new();
    public string? Warning { get; set; }
}
