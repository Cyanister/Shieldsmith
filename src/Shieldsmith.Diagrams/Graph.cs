namespace Shieldsmith.Diagrams;

/// <summary>
/// A directed graph to be laid out and drawn. Deliberately free of any Power
/// Platform concepts: callers translate their own model into this shape, which
/// keeps the layout engine testable and reusable.
/// </summary>
public sealed class Graph
{
    public List<Node> Nodes { get; } = new();
    public List<Edge> Edges { get; } = new();
    public LayoutDirection Direction { get; set; } = LayoutDirection.TopToBottom;
    public string? Title { get; set; }

    public Node AddNode(string id, string title)
    {
        var node = new Node { Id = id, Title = title };
        Nodes.Add(node);
        return node;
    }

    public Edge AddEdge(string fromId, string toId)
    {
        var edge = new Edge { FromId = fromId, ToId = toId };
        Edges.Add(edge);
        return edge;
    }

    public Node? Find(string id) =>
        Nodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
}

public enum LayoutDirection
{
    TopToBottom,
    LeftToRight,
}

public sealed class Node
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    /// <summary>Body rows drawn inside the box, e.g. a table's columns.</summary>
    public List<NodeLine> Lines { get; } = new();
    public NodeShape Shape { get; set; } = NodeShape.Record;
    /// <summary>Optional accent override; defaults to the theme's primary.</summary>
    public string? AccentColour { get; set; }

    // Assigned by the layout engine. Position is the centre of the box.
    public double X { get; internal set; }
    public double Y { get; internal set; }
    public double Width { get; internal set; }
    public double Height { get; internal set; }

    internal int Layer { get; set; }
    internal double Order { get; set; }
    internal bool IsDummy { get; set; }

    public double Left => X - Width / 2;
    public double Top => Y - Height / 2;
    public double Right => X + Width / 2;
    public double Bottom => Y + Height / 2;
}

public sealed class NodeLine
{
    public string Text { get; set; } = string.Empty;
    /// <summary>Short marker drawn in the accent colour, e.g. "PK".</summary>
    public string? Marker { get; set; }
    public bool Emphasise { get; set; }

    public NodeLine() { }
    public NodeLine(string text, string? marker = null, bool emphasise = false)
    {
        Text = text;
        Marker = marker;
        Emphasise = emphasise;
    }
}

public enum NodeShape
{
    /// <summary>Title bar plus body rows: the entity box in an ERD.</summary>
    Record,
    /// <summary>Rounded rectangle: a step in a flowchart.</summary>
    RoundedBox,
    /// <summary>Stadium: a start or end terminator.</summary>
    Stadium,
    /// <summary>Diamond: a condition.</summary>
    Diamond,
}

public sealed class Edge
{
    public string FromId { get; set; } = string.Empty;
    public string ToId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public bool Dashed { get; set; }
    public EdgeEnding FromEnding { get; set; } = EdgeEnding.None;
    public EdgeEnding ToEnding { get; set; } = EdgeEnding.Arrow;

    /// <summary>Polyline through which the edge is drawn, assigned by layout.</summary>
    public List<PointD> Waypoints { get; } = new();
    internal bool Reversed { get; set; }
}

public enum EdgeEnding
{
    None,
    Arrow,
    CrowsFoot,
}

public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
}
