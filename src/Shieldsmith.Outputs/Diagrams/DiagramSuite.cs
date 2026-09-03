using Shieldsmith.Core.Models;
using Shieldsmith.Diagrams;
using Shieldsmith.Outputs.Graphing;
using Shieldsmith.Outputs.Mermaid;

namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// Produces every diagram for a solution: the entity relationship diagram and a
/// flowchart per cloud flow, each as Mermaid source plus a rendered image.
///
/// Three engines, in preference order:
///   1. Mermaid, rendered through the injected renderer (real mermaid.js)
///   2. Shieldsmith's own layered layout engine (always available)
///   3. Graphviz, when the user has it and wants it
/// The built-in engine means a good diagram never depends on anything external.
/// </summary>
public static class DiagramSuite
{
    /// <summary>
    /// Renders Mermaid source to files. Supplied by the host (CLI or app) so
    /// this assembly stays free of a WebView2 dependency.
    /// </summary>
    public delegate DiagramImage? MermaidRenderer(string mermaidSource, string outputDirectory, string baseName);

    /// <summary>
    /// Above this many tables, an entity relationship diagram showing every
    /// column is a wall of text nobody can read: the boxes grow tall, the edges
    /// span the whole canvas, and the result is a hairball. Names only keeps it
    /// legible, and the per-table detail is in the document anyway.
    /// </summary>
    private const int ColumnsBecomeNoiseAbove = 25;

    public sealed class Options
    {
        public bool ShowColumns { get; set; } = true;

        /// <summary>
        /// Drops columns from a very large entity diagram. Off by default:
        /// asking for columns and silently not getting them is worse than a
        /// large diagram, and "show columns" is an explicit instruction.
        /// </summary>
        public bool SimplifyLargeDiagrams { get; set; }
        public bool IncludeFlowDiagrams { get; set; } = true;
        /// <summary>Cap on rendered flow charts; the rest still get Mermaid source.</summary>
        public int MaxFlowDiagrams { get; set; } = 60;
        /// <summary>
        /// Mermaid first, falling back to the built-in engine. Mermaid gives up
        /// quietly past its size limits, which is why its output is checked for
        /// an error diagram rather than trusted; when that happens the built-in
        /// engine draws it instead and a note says so.
        /// </summary>
        public DiagramEngine PreferredEngine { get; set; } = DiagramEngine.Mermaid;
        public DiagramTheme Theme { get; set; } = DiagramTheme.Brand;
    }

    public static DiagramSet Build(SolutionModel model, string outputDirectory,
        Options? options = null, MermaidRenderer? mermaidRenderer = null,
        IProgress<string>? progress = null)
    {
        options ??= new Options();
        Directory.CreateDirectory(outputDirectory);
        var set = new DiagramSet();

        // --- Entity relationship diagram -----------------------------------
        // No tables means no data model. An agent, flow or web resource solution
        // is a normal thing to document; drawing an empty diagram for it just
        // produces a blank box that looks like a failure.
        if (model.Entities.Count == 0)
        {
            set.Notes.Add("This solution contains no tables, so there is no entity relationship " +
                          "diagram to draw.");
            if (!options.IncludeFlowDiagrams) return set;
        }

        progress?.Report("Building the entity relationship diagram...");

        var showColumns = options.ShowColumns;
        if (showColumns && options.SimplifyLargeDiagrams &&
            model.Entities.Count > ColumnsBecomeNoiseAbove)
        {
            showColumns = false;
            set.Notes.Add($"The entity relationship diagram shows table names only: " +
                          $"{model.Entities.Count} tables with every column is not readable. " +
                          "Each table's columns are listed in full in its own section.");
        }

        set.ErdMermaidSource = model.Entities.Count == 0
            ? string.Empty
            : MermaidGenerator.BuildErd(model, showColumns);

        var erdImage = set.ErdMermaidSource.Length == 0
            ? null
            : TryMermaid(mermaidRenderer, set.ErdMermaidSource, outputDirectory, "erd",
                options, set, "entity relationship diagram");
        if (erdImage is null)
        {
            var graph = SolutionGraphBuilder.BuildErd(model, showColumns);
            if (graph.Nodes.Count > 0)
                erdImage = RenderInternal(graph, outputDirectory, "erd", options, set,
                    "entity relationship diagram");
        }
        set.Erd = erdImage;

        // --- One flowchart per cloud flow ----------------------------------
        if (!options.IncludeFlowDiagrams) return set;

        var flowDirectory = Path.Combine(outputDirectory, "flows");
        var rendered = 0;
        foreach (var process in model.Processes.Where(p => p.CloudFlow is not null))
        {
            var baseName = FileName(process.Name);
            var source = MermaidGenerator.BuildFlowchart(process);
            set.FlowMermaidSources[process.Name] = source;

            if (rendered >= options.MaxFlowDiagrams)
            {
                set.Notes.Add($"Flow diagrams were capped at {options.MaxFlowDiagrams}; " +
                              "Mermaid source is still written for every flow.");
                break;
            }

            progress?.Report($"Diagramming flow {process.Name}...");
            var image = TryMermaid(mermaidRenderer, source, flowDirectory, baseName,
                options, set, $"flow '{process.Name}'");
            if (image is null)
            {
                var graph = SolutionGraphBuilder.BuildFlow(process);
                if (graph.Nodes.Count > 1)
                    image = RenderInternal(graph, flowDirectory, baseName, options, set,
                        $"flow '{process.Name}'");
            }
            if (image is not null)
            {
                set.FlowDiagrams[process.Name] = image;
                rendered++;
            }
        }

        return set;
    }

    /// <summary>
    /// Draws with the built-in engine. One diagram failing must not lose the
    /// other fifty nine: a solution large enough to defeat the rasteriser is
    /// exactly the solution whose documentation is worth most.
    /// </summary>
    private static DiagramImage? RenderInternal(Graph graph, string outputDirectory,
        string baseName, Options options, DiagramSet set, string what)
    {
        try
        {
            var files = DiagramRenderer.Render(graph, outputDirectory, baseName, options.Theme);
            if (files.Warning is not null) set.Notes.Add(files.Warning);
            return new DiagramImage(files.SvgPath, files.PngPath, DiagramEngine.Internal);
        }
        catch (Exception ex)
        {
            set.Notes.Add($"The {what} could not be drawn ({ex.Message}). " +
                          "Everything else in this document is unaffected.");
            return null;
        }
    }

    private static DiagramImage? TryMermaid(MermaidRenderer? renderer, string source,
        string outputDirectory, string baseName, Options options, DiagramSet set, string what)
    {
        if (renderer is null || options.PreferredEngine != DiagramEngine.Mermaid) return null;
        try
        {
            var image = renderer(source, outputDirectory, baseName);
            if (image is null) NoteFallback(set, what, set.LastMermaidMessage);
            return image;
        }
        catch (Exception ex)
        {
            NoteFallback(set, what, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Records why Mermaid was not used, once, with the real reason attached so
    /// a silent fallback is never mistaken for a successful Mermaid render.
    /// </summary>
    private static void NoteFallback(DiagramSet set, string what, string? reason)
    {
        if (set.MermaidNoteAdded) return;
        var detail = string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason})";
        set.Notes.Add($"Mermaid was unavailable for the {what}{detail}; " +
                      "Shieldsmith's built-in engine drew the diagrams instead.");
        set.MermaidNoteAdded = true;
    }

    internal static string FileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '-' : c).ToArray());
        cleaned = cleaned.Trim('-').ToLowerInvariant();
        return cleaned.Length == 0 ? "diagram" : cleaned;
    }
}

public enum DiagramEngine
{
    Mermaid,
    Internal,
    Graphviz,
}

public sealed record DiagramImage(string SvgPath, string? PngPath, DiagramEngine Engine)
{
    /// <summary>True when a raster exists; the SVG is always written.</summary>
    public bool HasRaster => PngPath is not null && File.Exists(PngPath);
}

public sealed class DiagramSet
{
    public DiagramImage? Erd { get; set; }
    public string ErdMermaidSource { get; set; } = string.Empty;
    public Dictionary<string, DiagramImage> FlowDiagrams { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FlowMermaidSources { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notes { get; } = new();
    /// <summary>Set by the host's renderer so a fallback can explain itself.</summary>
    public string? LastMermaidMessage { get; set; }
    internal bool MermaidNoteAdded { get; set; }
}
