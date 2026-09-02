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
        /// <summary>Gap between a cluster's border and the nodes it encloses.</summary>
        public double ClusterPadding { get; set; } = 18;
        /// <summary>Space reserved above a cluster's contents for its label.</summary>
        public double ClusterLabelHeight { get; set; } = 20;
        /// <summary>
        /// Width reserved for a long edge passing through a layer. A dummy node
        /// one pixel wide gives an edge no corridor, so it ends up drawn against
        /// or under the boxes either side of it.
        /// </summary>
        public double EdgeCorridor { get; set; } = 18;
        /// <summary>
        /// Route edges as right angles through the empty band between layers
        /// rather than straight from centre to centre. A straight line cuts
        /// diagonally across the gap and clips whatever boxes are in the way.
        /// </summary>
        public bool OrthogonalEdges { get; set; } = true;
    }

    private readonly Options _options;

    public LayeredLayout(Options? options = null) => _options = options ?? new Options();

    public LayoutResult Apply(Graph graph, ITextMeasurer measurer)
    {
        foreach (var node in graph.Nodes)
            NodeMeasurement.Measure(node, measurer);

        // Cluster borders and their labels occupy the gaps, so a clustered graph
        // needs more room between layers and between siblings than a plain one.
        if (graph.Clusters.Count > 0)
        {
            _options.LayerGap = Math.Max(_options.LayerGap,
                _options.ClusterPadding * 2 + _options.ClusterLabelHeight + 24);
            _options.NodeGap = Math.Max(_options.NodeGap, _options.ClusterPadding * 2 + 16);
        }

        var working = WorkingGraph.From(graph);
        RemoveCycles(working);
        AssignLayers(working);
        var layers = BuildLayers(working, graph);
        ReduceCrossings(working, layers);
        GroupClusters(graph, working, layers);
        AssignCoordinates(graph, working, layers);
        ComputeClusterBounds(graph);
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

    // Note for anyone tempted to cap layer width here: spilling over-full layers
    // into the next one was tried and measured, and it makes the diagram wider,
    // not narrower. Pushing a node down lengthens every edge that crosses it,
    // and each extra layer an edge spans costs a dummy node that occupies width
    // in its own layer. On a 100 table solution it went from 35962 to 43100
    // pixels wide. A large entity relationship graph is not a DAG and wants a
    // different layout, not a tweaked layered one.

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

    // ---- 4b. Cluster grouping --------------------------------------------

    /// <summary>
    /// Reorders each layer so that nodes belonging to the same cluster sit next
    /// to each other. Without this a cluster's bounding box would swallow
    /// unrelated nodes that happened to be ordered between its members, and a
    /// scope would appear to contain steps that are not in it.
    ///
    /// Crossing reduction has already run, so the existing order is used as the
    /// ranking key: each cluster takes the average position of its members, and
    /// members move to sit together at that position. That keeps the layout the
    /// crossing pass found while making the grouping true.
    /// </summary>
    private static void GroupClusters(Graph graph, WorkingGraph g, List<List<int>> layers)
    {
        if (graph.Clusters.Count == 0) return;

        foreach (var layer in layers)
        {
            if (layer.Count < 2) continue;

            var position = layer.Select((index, i) => (index, i))
                .ToDictionary(p => p.index, p => (double)p.i);

            // Group by the outermost cluster, so nested scopes stay inside their
            // parent rather than being pulled out alongside it.
            string KeyFor(int index) => OutermostCluster(graph, g.NodeOf(index).ClusterId) ?? $"__none_{index}";

            var averages = layer.GroupBy(KeyFor)
                .ToDictionary(grp => grp.Key, grp => grp.Average(i => position[i]));

            layer.Sort((a, b) =>
            {
                var keyA = KeyFor(a);
                var keyB = KeyFor(b);
                if (keyA != keyB) return averages[keyA].CompareTo(averages[keyB]);
                return position[a].CompareTo(position[b]);
            });
        }
    }

    private static string? OutermostCluster(Graph graph, string? clusterId)
    {
        var cluster = graph.FindCluster(clusterId);
        if (cluster is null) return null;
        var guard = 0;
        while (cluster.ParentId is not null && guard++ < 32)
        {
            var parent = graph.FindCluster(cluster.ParentId);
            if (parent is null) break;
            cluster = parent;
        }
        return cluster.Id;
    }

    /// <summary>
    /// A cluster's box is the bounding box of its member nodes and of any
    /// clusters nested inside it, padded so the border does not touch them.
    /// Deeper clusters are padded less so nesting stays visible.
    /// </summary>
    private void ComputeClusterBounds(Graph graph)
    {
        if (graph.Clusters.Count == 0) return;

        // Deepest first, so a parent can expand around already-sized children.
        foreach (var cluster in graph.Clusters.OrderByDescending(graph.DepthOf))
        {
            var members = graph.Nodes.Where(n => !n.IsDummy && Encloses(graph, cluster.Id, n.ClusterId)).ToList();
            var nested = graph.Clusters
                .Where(c => c.ParentId == cluster.Id && c.HasBounds)
                .ToList();
            if (members.Count == 0 && nested.Count == 0) continue;

            var left = members.Select(n => n.Left).Concat(nested.Select(c => c.Left)).Min();
            var right = members.Select(n => n.Right).Concat(nested.Select(c => c.Right)).Max();
            var top = members.Select(n => n.Top).Concat(nested.Select(c => c.Top)).Min();
            var bottom = members.Select(n => n.Bottom).Concat(nested.Select(c => c.Bottom)).Max();

            var pad = _options.ClusterPadding;
            cluster.Left = left - pad;
            cluster.Right = right + pad;
            // Extra room at the top for the cluster's own label.
            cluster.Top = top - pad - _options.ClusterLabelHeight;
            cluster.Bottom = bottom + pad;
        }
    }

    /// <summary>True when <paramref name="nodeClusterId"/> is the cluster or nested inside it.</summary>
    private static bool Encloses(Graph graph, string clusterId, string? nodeClusterId)
    {
        var current = graph.FindCluster(nodeClusterId);
        var guard = 0;
        while (current is not null && guard++ < 32)
        {
            if (string.Equals(current.Id, clusterId, StringComparison.OrdinalIgnoreCase)) return true;
            current = graph.FindCluster(current.ParentId);
        }
        return false;
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

        Straighten(g, layers, cross, vertical);

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

    /// <summary>
    /// Pulls a node directly in line with its predecessor whenever the two are
    /// each other's only neighbour. A run of sequential steps is then drawn as a
    /// straight column rather than a staircase.
    ///
    /// The median relaxation alone leaves long chains drifting sideways, because
    /// nothing anchors a node whose neighbours have themselves moved. On a real
    /// flow with twenty-five consecutive Initialize variable actions that drift
    /// accumulated into a diagonal cascade across an otherwise empty canvas.
    /// </summary>
    private void Straighten(WorkingGraph g, List<List<int>> layers,
        Dictionary<int, double> cross, bool vertical)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var downward = pass % 2 == 0;
            var sequence = downward
                ? Enumerable.Range(0, layers.Count)
                : Enumerable.Range(0, layers.Count).Reverse();

            foreach (var layerIndex in sequence)
            {
                foreach (var node in layers[layerIndex])
                {
                    // Only align where the relationship is unambiguous: one edge
                    // in each direction. Anything branching keeps its median.
                    if (downward)
                    {
                        var incoming = g.InEdges(node).ToList();
                        if (incoming.Count != 1) continue;
                        var parent = incoming[0].From;
                        if (g.OutEdges(parent).Count() != 1) continue;
                        cross[node] = cross[parent];
                    }
                    else
                    {
                        var outgoing = g.OutEdges(node).ToList();
                        if (outgoing.Count != 1) continue;
                        var child = outgoing[0].To;
                        if (g.InEdges(child).Count() != 1) continue;
                        cross[node] = cross[child];
                    }
                }
                Separate(g, layers[layerIndex], cross, vertical);
            }
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

    private void RouteEdges(Graph graph, WorkingGraph g)
    {
        var vertical = graph.Direction == LayoutDirection.TopToBottom;

        foreach (var edge in graph.Edges)
        {
            edge.Waypoints.Clear();
            var chain = g.ChainFor(edge);
            if (chain.Count < 2) continue;

            var points = chain.Select(i => new PointD(g.NodeOf(i).X, g.NodeOf(i).Y)).ToList();
            if (g.WasReversed(edge)) points.Reverse();

            var from = graph.Find(edge.FromId);
            var to = graph.Find(edge.ToId);

            if (_options.OrthogonalEdges && from is not null && to is not null)
            {
                edge.Waypoints.AddRange(Orthogonal(from, to, points, vertical));
                continue;
            }

            // Trim the first and last segment to the boundary of the real
            // endpoints so lines meet the box edge rather than its centre.
            if (from is not null && points.Count >= 2)
                points[0] = BoundaryPoint(from, points[1]);
            if (to is not null && points.Count >= 2)
                points[^1] = BoundaryPoint(to, points[^2]);

            edge.Waypoints.AddRange(points);
        }
    }

    /// <summary>
    /// Routes an edge as right angles: straight out of the source's trailing
    /// face, across in the empty band between layers, then straight into the
    /// target's leading face.
    ///
    /// The band between two layers contains no boxes by construction, so a line
    /// that only ever travels sideways inside it cannot cross one. A straight
    /// centre-to-centre line has no such guarantee, which is why long
    /// relationships in an entity diagram used to run under the tables between
    /// their endpoints.
    /// </summary>
    private static IEnumerable<PointD> Orthogonal(Node from, Node to,
        IReadOnlyList<PointD> chain, bool vertical)
    {
        var points = new List<PointD>();

        // Interior points come from the dummy nodes, which already sit in
        // reserved corridors; the corridor is what keeps them clear of boxes.
        var waypoints = chain.Count > 2 ? chain.Skip(1).Take(chain.Count - 2).ToList() : new List<PointD>();

        if (vertical)
        {
            var start = new PointD(from.X, from.Bottom);
            var end = new PointD(to.X, to.Top);
            // Going upwards means the edge was reversed; leave from the top.
            if (end.Y < start.Y)
            {
                start = new PointD(from.X, from.Top);
                end = new PointD(to.X, to.Bottom);
            }

            points.Add(start);
            var previous = start;
            foreach (var waypoint in waypoints)
            {
                points.Add(new PointD(previous.X, waypoint.Y));
                points.Add(new PointD(waypoint.X, waypoint.Y));
                previous = new PointD(waypoint.X, waypoint.Y);
            }

            // Turn in the middle of the remaining gap so the corner is clear of
            // both boxes rather than hard against one of them.
            var turn = (previous.Y + end.Y) / 2;
            if (Math.Abs(previous.X - end.X) > 0.5)
            {
                points.Add(new PointD(previous.X, turn));
                points.Add(new PointD(end.X, turn));
            }
            points.Add(end);
        }
        else
        {
            var start = new PointD(from.Right, from.Y);
            var end = new PointD(to.Left, to.Y);
            if (end.X < start.X)
            {
                start = new PointD(from.Left, from.Y);
                end = new PointD(to.Right, to.Y);
            }

            points.Add(start);
            var previous = start;
            foreach (var waypoint in waypoints)
            {
                points.Add(new PointD(waypoint.X, previous.Y));
                points.Add(new PointD(waypoint.X, waypoint.Y));
                previous = new PointD(waypoint.X, waypoint.Y);
            }

            var turn = (previous.X + end.X) / 2;
            if (Math.Abs(previous.Y - end.Y) > 0.5)
            {
                points.Add(new PointD(turn, previous.Y));
                points.Add(new PointD(turn, end.Y));
            }
            points.Add(end);
        }

        return Simplify(points);
    }

    /// <summary>Drops points that repeat or sit on a straight run.</summary>
    private static List<PointD> Simplify(IReadOnlyList<PointD> points)
    {
        var result = new List<PointD>();
        foreach (var point in points)
        {
            if (result.Count > 0 &&
                Math.Abs(result[^1].X - point.X) < 0.01 &&
                Math.Abs(result[^1].Y - point.Y) < 0.01)
                continue;
            result.Add(point);
        }

        for (var i = result.Count - 2; i > 0; i--)
        {
            var before = result[i - 1];
            var here = result[i];
            var after = result[i + 1];
            var collinearX = Math.Abs(before.X - here.X) < 0.01 && Math.Abs(here.X - after.X) < 0.01;
            var collinearY = Math.Abs(before.Y - here.Y) < 0.01 && Math.Abs(here.Y - after.Y) < 0.01;
            if (collinearX || collinearY) result.RemoveAt(i);
        }
        return result;
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

        // A cluster border sits outside its members, so it can be the outermost
        // thing on the canvas.
        foreach (var cluster in graph.Clusters.Where(c => c.HasBounds))
        {
            width = Math.Max(width, cluster.Right + _options.Margin);
            height = Math.Max(height, cluster.Bottom + _options.Margin);
        }

        return new LayoutResult(Math.Max(width, 200), Math.Max(height, 120));
    }
}

public readonly record struct LayoutResult(double Width, double Height);
