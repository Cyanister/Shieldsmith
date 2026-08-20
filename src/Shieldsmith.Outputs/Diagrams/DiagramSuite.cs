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

    public sealed class Options
    {
        public bool ShowColumns { get; set; } = true;
        public bool IncludeFlowDiagrams { get; set; } = true;
        /// <summary>Cap on rendered flow charts; the rest still get Mermaid source.</summary>
        public int MaxFlowDiagrams { get; set; } = 60;
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
        progress?.Report("Building the entity relationship diagram...");
        set.ErdMermaidSource = MermaidGenerator.BuildErd(model, options.ShowColumns);

        var erdImage = TryMermaid(mermaidRenderer, set.ErdMermaidSource, outputDirectory, "erd",
            options, set, "entity relationship diagram");
        if (erdImage is null)
        {
            var graph = SolutionGraphBuilder.BuildErd(model, options.ShowColumns);
            if (graph.Nodes.Count > 0)
            {
                var files = DiagramRenderer.Render(graph, outputDirectory, "erd", options.Theme);
                erdImage = new DiagramImage(files.SvgPath, files.PngPath, DiagramEngine.Internal);
            }
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
                {
                    var files = DiagramRenderer.Render(graph, flowDirectory, baseName, options.Theme);
                    image = new DiagramImage(files.SvgPath, files.PngPath, DiagramEngine.Internal);
                }
            }
            if (image is not null)
            {
                set.FlowDiagrams[process.Name] = image;
                rendered++;
            }
        }

        return set;
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

public sealed record DiagramImage(string SvgPath, string? PngPath, DiagramEngine Engine);

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
