using System.Globalization;
using Shieldsmith.Diagrams;
using Shieldsmith.Diagrams.Layout;
using Shieldsmith.Diagrams.Rendering;
using Xunit;

namespace Shieldsmith.Diagrams.Tests;

public sealed class LayoutTests
{
    private static Graph Chain(int length)
    {
        var graph = new Graph();
        for (var i = 0; i < length; i++) graph.AddNode($"n{i}", $"Node {i}");
        for (var i = 1; i < length; i++) graph.AddEdge($"n{i - 1}", $"n{i}");
        return graph;
    }

    [Fact]
    public void Chain_is_laid_out_in_order_down_the_page()
    {
        var graph = Chain(5);
        DiagramRenderer.Layout(graph);

        var ys = graph.Nodes.Select(n => n.Y).ToList();
        Assert.Equal(ys.OrderBy(y => y), ys); // each node strictly below the last
        Assert.All(graph.Nodes, n => Assert.True(n.Width > 0 && n.Height > 0));
    }

    [Fact]
    public void No_node_is_positioned_outside_the_canvas()
    {
        // A wide fan-out used to push the leftmost node off the left edge,
        // because normalisation worked on centres rather than box edges.
        var graph = new Graph();
        graph.AddNode("root", "Root with quite a long title");
        for (var i = 0; i < 6; i++)
        {
            graph.AddNode($"leaf{i}", $"Leaf number {i} with a long label");
            graph.AddEdge("root", $"leaf{i}");
        }

        var size = DiagramRenderer.Layout(graph);

        Assert.All(graph.Nodes, n =>
        {
            Assert.True(n.Left >= 0, $"{n.Id} extends past the left edge ({n.Left}).");
            Assert.True(n.Top >= 0, $"{n.Id} extends above the top edge ({n.Top}).");
            Assert.True(n.Right <= size.Width, $"{n.Id} extends past the right edge.");
            Assert.True(n.Bottom <= size.Height, $"{n.Id} extends past the bottom edge.");
        });
    }

    [Fact]
    public void Cycles_do_not_hang_the_layout()
    {
        var graph = new Graph();
        graph.AddNode("a", "A");
        graph.AddNode("b", "B");
        graph.AddNode("c", "C");
        graph.AddEdge("a", "b");
        graph.AddEdge("b", "c");
        graph.AddEdge("c", "a"); // back edge

        var size = DiagramRenderer.Layout(graph);
        Assert.True(size.Width > 0 && size.Height > 0);
        Assert.All(graph.Edges, e => Assert.True(e.Waypoints.Count >= 2));
    }

    [Fact]
    public void Node_height_matches_the_content_drawn_inside_it()
    {
        var graph = new Graph();
        var node = graph.AddNode("t", "Table");
        node.Subtitle = "logical_name";
        for (var i = 0; i < 4; i++) node.Lines.Add(new NodeLine($"Column {i}: Text"));

        DiagramRenderer.Layout(graph);

        var expected = NodeMeasurement.HeaderHeight(node)
                       + NodeMeasurement.PaddingY
                       + 4 * (NodeMeasurement.LineFontSize + NodeMeasurement.LineSpacing)
                       + NodeMeasurement.PaddingY;
        Assert.Equal(expected, node.Height, 1);
    }

    [Fact]
    public void Edge_label_sits_between_the_nodes_not_on_one()
    {
        var graph = Chain(2);
        graph.Edges[0].Label = "lookup";
        DiagramRenderer.Layout(graph);

        var midpoint = SvgRenderer.Midpoint(graph.Edges[0]);
        var from = graph.Nodes[0];
        var to = graph.Nodes[1];

        Assert.True(midpoint.Y > from.Bottom, "The label overlaps the source node.");
        Assert.True(midpoint.Y < to.Top, "The label overlaps the target node.");
    }

    [Fact]
    public void Svg_output_is_culture_invariant()
    {
        // A comma decimal separator would produce structurally broken SVG.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var graph = Chain(3);
            var size = DiagramRenderer.Layout(graph);
            var svg = SvgRenderer.Render(graph, size);

            Assert.StartsWith("<svg", svg);
            Assert.DoesNotContain(",5\"", svg);   // e.g. width="12,5"
            Assert.Contains("</svg>", svg);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Renders_both_svg_and_png_files()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"shieldsmith-diagram-{Guid.NewGuid():N}");
        try
        {
            var graph = Chain(4);
            graph.Nodes[0].Shape = NodeShape.Stadium;
            graph.Nodes[2].Shape = NodeShape.Diamond;

            var files = DiagramRenderer.Render(graph, directory, "test");

            Assert.True(File.Exists(files.SvgPath));
            Assert.True(File.Exists(files.PngPath));
            Assert.True(new FileInfo(files.PngPath).Length > 1000);
            Assert.Contains("<svg", File.ReadAllText(files.SvgPath));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { /* best effort */ }
        }
    }
}
