namespace Shieldsmith.Outputs.Diagrams;

/// <summary>
/// A laid-out entity relationship diagram: entity boxes with positions and sizes
/// in inches, plus the edges between them. Produced either by Graphviz
/// (dot -Tplain) or by the built-in fallback layout, and consumed by the
/// fallback PNG renderer and the VSDX writer.
/// </summary>
public sealed class ErdLayout
{
    /// <summary>Total canvas size in inches.</summary>
    public double Width { get; set; }
    public double Height { get; set; }
    public List<ErdNode> Nodes { get; } = new();
    public List<ErdEdge> Edges { get; } = new();

    public ErdNode? FindNode(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
}

public sealed class ErdNode
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public List<string> Lines { get; } = new();
    /// <summary>Centre position in inches, origin bottom-left (Graphviz convention).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

public sealed class ErdEdge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool Dashed { get; set; }
}
