namespace Shieldsmith.Diagrams.Layout;

/// <summary>
/// Index-based mutable view of a <see cref="Graph"/> used during layout. Real
/// nodes keep their identity; dummy nodes are appended so an edge crossing
/// several layers becomes a chain of single-layer segments.
/// </summary>
internal sealed class WorkingGraph
{
    private readonly List<Node> _nodes = new();
    private readonly List<WorkingEdge> _edges = new();
    private readonly Dictionary<int, int> _layer = new();
    private readonly Dictionary<int, double> _order = new();
    private readonly Dictionary<Edge, bool> _reversed = new();

    public IEnumerable<int> NodeIndices => Enumerable.Range(0, _nodes.Count);
    public IReadOnlyList<WorkingEdge> Edges => _edges;

    public static WorkingGraph From(Graph graph)
    {
        var working = new WorkingGraph();
        var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            indexOf[node.Id] = working._nodes.Count;
            working._nodes.Add(node);
        }
        foreach (var index in working.NodeIndices)
        {
            working._layer[index] = 0;
            working._order[index] = index;
        }

        foreach (var edge in graph.Edges)
        {
            if (!indexOf.TryGetValue(edge.FromId, out var from)) continue;
            if (!indexOf.TryGetValue(edge.ToId, out var to)) continue;
            if (from == to) continue; // self-loops are drawn separately, not laid out
            working._edges.Add(new WorkingEdge { From = from, To = to, Owner = edge });
            working._reversed[edge] = false;
        }
        return working;
    }

    public Node NodeOf(int index) => _nodes[index];
    public int LayerOf(int index) => _layer[index];
    public void SetLayer(int index, int layer) => _layer[index] = layer;
    public double OrderOf(int index) => _order.TryGetValue(index, out var value) ? value : 0;
    public void SetOrder(int index, double order) => _order[index] = order;

    public IEnumerable<WorkingEdge> OutEdges(int index) => _edges.Where(e => e.From == index);
    public IEnumerable<WorkingEdge> InEdges(int index) => _edges.Where(e => e.To == index);

    public void ReverseEdge(WorkingEdge edge)
    {
        (edge.From, edge.To) = (edge.To, edge.From);
        _reversed[edge.Owner] = !_reversed[edge.Owner];
    }

    public bool WasReversed(Edge edge) => _reversed.TryGetValue(edge, out var value) && value;

    public void AddEdge(int from, int to, Edge owner, bool isSegment) =>
        _edges.Add(new WorkingEdge { From = from, To = to, Owner = owner, IsSegment = isSegment });

    public void RemoveEdge(WorkingEdge edge) => _edges.Remove(edge);

    public int AddDummy(int layer, Graph graph)
    {
        var node = new Node
        {
            Id = $"__dummy_{_nodes.Count}",
            IsDummy = true,
            Width = 1,
            Height = 1,
        };
        var index = _nodes.Count;
        _nodes.Add(node);
        _layer[index] = layer;
        _order[index] = index;
        return index;
    }

    /// <summary>
    /// The ordered chain of node indices an original edge travels through,
    /// including any dummy nodes inserted for it.
    /// </summary>
    public List<int> ChainFor(Edge edge)
    {
        var segments = _edges.Where(e => e.Owner == edge).ToList();
        if (segments.Count == 0) return new List<int>();
        if (segments.Count == 1) return new List<int> { segments[0].From, segments[0].To };

        var next = segments.ToDictionary(s => s.From, s => s.To);
        var destinations = segments.Select(s => s.To).ToHashSet();
        var start = segments.Select(s => s.From).FirstOrDefault(f => !destinations.Contains(f), segments[0].From);

        var chain = new List<int> { start };
        var guard = 0;
        while (next.TryGetValue(chain[^1], out var following) && guard++ < segments.Count + 1)
            chain.Add(following);
        return chain;
    }
}

internal sealed class WorkingEdge
{
    public int From { get; set; }
    public int To { get; set; }
    public Edge Owner { get; set; } = null!;
    public bool Reversed { get; set; }
    public bool IsSegment { get; set; }
}
