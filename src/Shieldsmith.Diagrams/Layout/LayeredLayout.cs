namespace Shieldsmith.Diagrams.Layout;

/// <summary>
/// A layered (Sugiyama-style) graph layout: break cycles, assign layers, insert
/// dummy nodes so every edge spans one layer, reduce crossings with the median
/// heuristic, then place nodes and route edges as polylines.
///
/// This is Shieldsmith's own engine. It exists so a good diagram never depends on
/// Graphviz being installed, and so flow charts and entity diagrams share one
/// well-behaved layout rather than a naive grid.
/// </summary>
public sealed class LayeredLayout
{
    public sealed class Options
    {
        /// <summary>Gap between adjacent layers, along the flow direction.</summary>
        public double LayerGap { get; set; } = 70;
        /// <summary>Gap between siblings within a layer, across the flow direction.</summary>
        public double NodeGap { get; set; } = 40;
        public double Margin { get; set; } = 32;
        /// <summary>Crossing-reduction sweeps. Four is plenty for solution-sized graphs.</summary>
        public int CrossingReductionPasses { get; set; } = 4;
        public int CoordinatePasses { get; set; } = 8;
    }

    private readonly Options _options;

    public LayeredLayout(Options? options = null) => _options = options ?? new Options();

    public LayoutResult Apply(Graph graph, ITextMeasurer measurer)
    {
        foreach (var node in graph.Nodes)
            NodeMeasurement.Measure(node, measurer);

        var working = WorkingGraph.From(graph);
        RemoveCycles(working);
        AssignLayers(working);
        var layers = BuildLayers(working, graph);
        ReduceCrossings(working, layers);
        AssignCoordinates(graph, working, layers);
        RouteEdges(graph, working);

        return Finalise(graph);
    }

    // ---- 1. Cycle removal -------------------------------------------------
    // Depth-first; any edge that points back at a node still on the stack is a
    // back edge and gets reversed for layering, then restored when routing.

    private static void RemoveCycles(WorkingGraph g)
    {
        var state = new Dictionary<int, int>(); // 0 unvisited, 1 on stack, 2 done
        foreach (var index in g.NodeIndices) state[index] = 0;

        void Visit(int index)
        {
            state[index] = 1;
            foreach (var edge in g.OutEdges(index).ToList())
            {
                if (state[edge.To] == 1)
                {
                    edge.Reversed = true;
                    g.ReverseEdge(edge);
                }
                else if (state[edge.To] == 0)
                {
                    Visit(edge.To);
                }
            }
            state[index] = 2;
        }

        foreach (var index in g.NodeIndices.ToList())
            if (state[index] == 0)
                Visit(index);
    }

    // ---- 2. Layer assignment ---------------------------------------------
    // Longest-path layering: a node sits one below its deepest predecessor.

    private static void AssignLayers(WorkingGraph g)
    {
        var layer = g.NodeIndices.ToDictionary(i => i, _ => 0);
        var order = TopologicalOrder(g);
        foreach (var index in order)
        {
            foreach (var edge in g.OutEdges(index))
            {
                if (layer[edge.To] < layer[index] + 1)
                    layer[edge.To] = layer[index] + 1;
            }
        }
        foreach (var (index, value) in layer) g.SetLayer(index, value);
    }

    private static List<int> TopologicalOrder(WorkingGraph g)
    {
        var inDegree = g.NodeIndices.ToDictionary(i => i, i => g.InEdges(i).Count());
        var queue = new Queue<int>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var order = new List<int>();
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            order.Add(index);
            foreach (var edge in g.OutEdges(index))
            {
                if (--inDegree[edge.To] == 0) queue.Enqueue(edge.To);
            }
        }
        // Any node left has an in-degree the sweep never cleared; append it so
        // no node is silently dropped from the layout.
        order.AddRange(g.NodeIndices.Except(order));
        return order;
    }

    // ---- 3. Normalise: dummy nodes for edges spanning several layers ------

    private static List<List<int>> BuildLayers(WorkingGraph g, Graph graph)
    {
        foreach (var edge in g.Edges.ToList())
        {
            var span = g.LayerOf(edge.To) - g.LayerOf(edge.From);
            if (span <= 1) continue;

            var previous = edge.From;
            for (var layer = g.LayerOf(edge.From) + 1; layer < g.LayerOf(edge.To); layer++)
            {
                var dummy = g.AddDummy(layer, graph);
                g.AddEdge(previous, dummy, edge.Owner, isSegment: true);
                previous = dummy;
            }
            g.AddEdge(previous, edge.To, edge.Owner, isSegment: true);
            g.RemoveEdge(edge);
        }

        var maxLayer = g.NodeIndices.Select(g.LayerOf).DefaultIfEmpty(0).Max();
        var layers = new List<List<int>>();
        for (var i = 0; i <= maxLayer; i++)
            layers.Add(g.NodeIndices.Where(n => g.LayerOf(n) == i).ToList());
        return layers;
    }

    // ---- 4. Crossing reduction -------------------------------------------
    // Median heuristic, sweeping down then up, keeping the best ordering seen.

    private void ReduceCrossings(WorkingGraph g, List<List<int>> layers)
    {
        for (var i = 0; i < layers.Count; i++)
            for (var j = 0; j < layers[i].Count; j++)
                g.SetOrder(layers[i][j], j);

        var best = layers.Select(l => l.ToList()).ToList();
        var bestCrossings = CountCrossings(g, layers);

        for (var pass = 0; pass < _options.CrossingReductionPasses; pass++)
        {
            var downward = pass % 2 == 0;
            if (downward)
            {
                for (var i = 1; i < layers.Count; i++) OrderByMedian(g, layers, i, lookUp: true);
            }
            else
            {
                for (var i = layers.Count - 2; i >= 0; i--) OrderByMedian(g, layers, i, lookUp: false);
            }

            var crossings = CountCrossings(g, layers);
            if (crossings < bestCrossings)
            {
                bestCrossings = crossings;
                best = layers.Select(l => l.ToList()).ToList();
            }
        }

        for (var i = 0; i < layers.Count; i++)
        {
            layers[i] = best[i];
            for (var j = 0; j < layers[i].Count; j++) g.SetOrder(layers[i][j], j);
        }
    }

    private static void OrderByMedian(WorkingGraph g, List<List<int>> layers, int layerIndex, bool lookUp)
    {
        var layer = layers[layerIndex];
        var medians = new Dictionary<int, double>();
        foreach (var node in layer)
        {
            var neighbours = (lookUp
                    ? g.InEdges(node).Select(e => e.From)
                    : g.OutEdges(node).Select(e => e.To))
                .Select(g.OrderOf)
                .OrderBy(v => v)
                .ToList();
            medians[node] = neighbours.Count == 0
                ? g.OrderOf(node) // no neighbours: hold position
                : neighbours[neighbours.Count / 2];
        }

        layer.Sort((a, b) =>
        {
            var comparison = medians[a].CompareTo(medians[b]);
            return comparison != 0 ? comparison : g.OrderOf(a).CompareTo(g.OrderOf(b));
        });

        for (var i = 0; i < layer.Count; i++) g.SetOrder(layer[i], i);
    }

    private static int CountCrossings(WorkingGraph g, List<List<int>> layers)
    {
        var total = 0;
        for (var i = 0; i + 1 < layers.Count; i++)
        {
            var pairs = new List<(double Upper, double Lower)>();
            foreach (var node in layers[i])
                foreach (var edge in g.OutEdges(node))
                    pairs.Add((g.OrderOf(edge.From), g.OrderOf(edge.To)));

            for (var a = 0; a < pairs.Count; a++)
                for (var b = a + 1; b < pairs.Count; b++)
                    if ((pairs[a].Upper - pairs[b].Upper) * (pairs[a].Lower - pairs[b].Lower) < 0)
                        total++;
        }
        return total;
    }

    // ---- 5. Coordinate assignment ----------------------------------------
    // Cross-axis position starts packed, then relaxes towards the median of
    // each node's neighbours while never letting siblings overlap.

    private void AssignCoordinates(Graph graph, WorkingGraph g, List<List<int>> layers)
    {
        var vertical = graph.Direction == LayoutDirection.TopToBottom;
        var cross = new Dictionary<int, double>();

        // Initial packing within each layer.
        foreach (var layer in layers)
        {
            var position = 0.0;
            foreach (var node in layer)
            {
                var extent = CrossExtent(g, node, vertical);
                cross[node] = position + extent / 2;
                position += extent + _options.NodeGap;
            }
        }

        for (var pass = 0; pass < _options.CoordinatePasses; pass++)
        {
            var downward = pass % 2 == 0;
            var sequence = downward
                ? Enumerable.Range(0, layers.Count)
                : Enumerable.Range(0, layers.Count).Reverse();

            foreach (var layerIndex in sequence)
            {
                foreach (var node in layers[layerIndex])
                {
                    var neighbours = (downward
                            ? g.InEdges(node).Select(e => e.From)
                            : g.OutEdges(node).Select(e => e.To))
                        .Select(n => cross[n])
                        .OrderBy(v => v)
                        .ToList();
                    if (neighbours.Count == 0) continue;
                    cross[node] = neighbours.Count % 2 == 1
                        ? neighbours[neighbours.Count / 2]
                        : (neighbours[neighbours.Count / 2 - 1] + neighbours[neighbours.Count / 2]) / 2;
                }
                Separate(g, layers[layerIndex], cross, vertical);
            }
        }

        // Main-axis position: stack layers by the tallest node in each.
        var main = _options.Margin;
        var layerMain = new double[layers.Count];
        for (var i = 0; i < layers.Count; i++)
        {
            var depth = layers[i]
                .Select(n => MainExtent(g, n, vertical))
                .DefaultIfEmpty(0)
                .Max();
            layerMain[i] = main + depth / 2;
            main += depth + _options.LayerGap;
        }

        // Shift by the leftmost *edge*, not the leftmost centre: normalising on
        // centres pushes half of the widest node off the canvas.
        var minEdge = g.NodeIndices
            .Select(i => cross[i] - CrossExtent(g, i, vertical) / 2)
            .DefaultIfEmpty(0)
            .Min();

        foreach (var index in g.NodeIndices)
        {
            var node = g.NodeOf(index);
            var crossValue = cross[index] - minEdge + _options.Margin;
            var mainValue = layerMain[g.LayerOf(index)];
            if (vertical) { node.X = crossValue; node.Y = mainValue; }
            else { node.X = mainValue; node.Y = crossValue; }
        }
    }

    /// <summary>Pushes overlapping siblings apart, preserving their order.</summary>
    private void Separate(WorkingGraph g, List<int> layer, Dictionary<int, double> cross, bool vertical)
    {
        for (var i = 1; i < layer.Count; i++)
        {
            var previous = layer[i - 1];
            var current = layer[i];
            var minimum = cross[previous]
                          + CrossExtent(g, previous, vertical) / 2
                          + _options.NodeGap
                          + CrossExtent(g, current, vertical) / 2;
            if (cross[current] < minimum) cross[current] = minimum;
        }
        for (var i = layer.Count - 2; i >= 0; i--)
        {
            var next = layer[i + 1];
            var current = layer[i];
            var maximum = cross[next]
                          - CrossExtent(g, next, vertical) / 2
                          - _options.NodeGap
                          - CrossExtent(g, current, vertical) / 2;
            if (cross[current] > maximum) cross[current] = maximum;
        }
    }

    private static double CrossExtent(WorkingGraph g, int index, bool vertical)
    {
        var node = g.NodeOf(index);
        return vertical ? node.Width : node.Height;
    }

    private static double MainExtent(WorkingGraph g, int index, bool vertical)
    {
        var node = g.NodeOf(index);
        return vertical ? node.Height : node.Width;
    }

    // ---- 6. Edge routing --------------------------------------------------

    private static void RouteEdges(Graph graph, WorkingGraph g)
    {
        foreach (var edge in graph.Edges)
        {
            edge.Waypoints.Clear();
            var chain = g.ChainFor(edge);
            if (chain.Count < 2) continue;

            var points = chain.Select(i => new PointD(g.NodeOf(i).X, g.NodeOf(i).Y)).ToList();
            if (g.WasReversed(edge)) points.Reverse();

            // Trim the first and last segment to the boundary of the real
            // endpoints so lines meet the box edge rather than its centre.
            var from = graph.Find(edge.FromId);
            var to = graph.Find(edge.ToId);
            if (from is not null && points.Count >= 2)
                points[0] = BoundaryPoint(from, points[1]);
            if (to is not null && points.Count >= 2)
                points[^1] = BoundaryPoint(to, points[^2]);

            edge.Waypoints.AddRange(points);
        }
    }

    /// <summary>Where a line aimed at <paramref name="towards"/> leaves the node's box.</summary>
    private static PointD BoundaryPoint(Node node, PointD towards)
    {
        var dx = towards.X - node.X;
        var dy = towards.Y - node.Y;
        if (Math.Abs(dx) < 0.0001 && Math.Abs(dy) < 0.0001) return new PointD(node.X, node.Y);

        var halfWidth = node.Width / 2;
        var halfHeight = node.Height / 2;
        var scaleX = dx == 0 ? double.MaxValue : halfWidth / Math.Abs(dx);
        var scaleY = dy == 0 ? double.MaxValue : halfHeight / Math.Abs(dy);
        var scale = Math.Min(scaleX, scaleY);
        return new PointD(node.X + dx * scale, node.Y + dy * scale);
    }

    private LayoutResult Finalise(Graph graph)
    {
        var nodes = graph.Nodes;
        var width = nodes.Select(n => n.Right).DefaultIfEmpty(0).Max() + _options.Margin;
        var height = nodes.Select(n => n.Bottom).DefaultIfEmpty(0).Max() + _options.Margin;

        foreach (var edge in graph.Edges)
        {
            foreach (var point in edge.Waypoints)
            {
                width = Math.Max(width, point.X + _options.Margin);
                height = Math.Max(height, point.Y + _options.Margin);
            }
        }

        return new LayoutResult(Math.Max(width, 200), Math.Max(height, 120));
    }
}

public readonly record struct LayoutResult(double Width, double Height);
