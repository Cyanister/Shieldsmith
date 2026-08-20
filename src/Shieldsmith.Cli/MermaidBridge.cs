using Shieldsmith.Outputs.Diagrams;

namespace Shieldsmith.Cli;

/// <summary>
/// Adapts the WebView2-backed Mermaid renderer to the delegate Shieldsmith.Outputs
/// expects, so the output assembly never has to reference WebView2 itself.
/// </summary>
internal static class MermaidBridge
{
    /// <summary>Set to the last failure reason so callers can report it rather than guess.</summary>
    public static string? LastMessage { get; private set; }

    public static DiagramSuite.MermaidRenderer? Create(bool enabled, out string status)
    {
        if (!enabled)
        {
            status = "Mermaid disabled; using Shieldsmith's built-in engine.";
            return null;
        }

        if (!Shieldsmith.Mermaid.MermaidRenderer.IsAvailable)
        {
            status = "WebView2 runtime not found; using Shieldsmith's built-in engine.";
            return null;
        }

        var renderer = new Shieldsmith.Mermaid.MermaidRenderer();
        status = $"Mermaid rendering via WebView2 {Shieldsmith.Mermaid.MermaidRenderer.RuntimeVersion}.";

        return (source, outputDirectory, baseName) =>
        {
            var result = renderer.Render(source, outputDirectory, baseName);
            if (result.Success && result.SvgPath is not null)
                return new DiagramImage(result.SvgPath, result.PngPath, DiagramEngine.Mermaid);
            LastMessage = result.Message;
            return null;
        };
    }
}
