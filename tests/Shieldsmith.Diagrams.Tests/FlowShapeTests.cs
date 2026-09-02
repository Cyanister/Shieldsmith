using Shieldsmith.Core.Models;
using Shieldsmith.Diagrams;
using Shieldsmith.Diagrams.Rendering;
using Shieldsmith.Outputs.Graphing;
using Xunit;

namespace Shieldsmith.Diagrams.Tests;

/// <summary>
/// The diagram should have the shape of the flow: containers drawn as boxes
/// around their contents, and branches side by side rather than stacked.
/// </summary>
public sealed class FlowShapeTests
{
    private static ProcessModel Flow(params FlowAction[] actions)
    {
        var process = new ProcessModel { Name = "Test", Category = 5 };
        process.CloudFlow = new CloudFlowDetail
        {
            Trigger = new FlowTrigger { Name = "t", Type = "Request" },
        };
        process.CloudFlow.Actions.AddRange(actions);
        return process;
    }

    private static FlowAction Branching(string name, string type, params (string Label, string[] Steps)[] branches)
    {
        var action = new FlowAction { Name = name, Type = type };
        foreach (var (label, steps) in branches)
        {
            var branch = new FlowBranch(label);
            foreach (var step in steps)
                branch.Actions.Add(new FlowAction { Name = step, Type = "Compose" });
            action.Branches.Add(branch);
        }
        return action;
    }

    [Fact]
    public void A_scope_becomes_a_cluster_containing_its_actions()
    {
        var scope = Branching("Try", "Scope", (string.Empty, new[] { "First", "Second" }));
        var graph = SolutionGraphBuilder.BuildFlow(Flow(scope));

        var cluster = Assert.Single(graph.Clusters);
        Assert.Equal("Try", cluster.Label);
        Assert.Equal("Scope", cluster.Subtitle);

        var members = graph.Nodes.Where(n => n.ClusterId == cluster.Id).Select(n => n.Title).ToList();
        Assert.Equal(new[] { "First", "Second" }, members);

        // The scope's own node sits outside the box it heads.
        Assert.Null(graph.Nodes.First(n => n.Title == "Try").ClusterId);
    }

    [Fact]
    public void A_condition_puts_each_branch_in_its_own_nested_box()
    {
        var condition = Branching("Condition", "If",
            ("Yes", new[] { "Send email" }),
            ("No", new[] { "Terminate" }));
        var graph = SolutionGraphBuilder.BuildFlow(Flow(condition));

        var outer = graph.Clusters.Single(c => c.ParentId is null);
        var nested = graph.Clusters.Where(c => c.ParentId == outer.Id).ToList();
        Assert.Equal(new[] { "Yes", "No" }, nested.Select(c => c.Label).ToArray());

        var yes = graph.Nodes.Single(n => n.Title == "Send email");
        var no = graph.Nodes.Single(n => n.Title == "Terminate");
        Assert.NotEqual(yes.ClusterId, no.ClusterId);
    }

    [Fact]
    public void Both_branches_leave_the_condition_directly_rather_than_chaining()
    {
        // The whole point: "No" must not hang off the end of "Yes".
        var condition = Branching("Condition", "If",
            ("Yes", new[] { "A" }),
            ("No", new[] { "B" }));
        var graph = SolutionGraphBuilder.BuildFlow(Flow(condition));

        var conditionId = graph.Nodes.Single(n => n.Title == "Condition").Id;
        var a = graph.Nodes.Single(n => n.Title == "A").Id;
        var b = graph.Nodes.Single(n => n.Title == "B").Id;

        Assert.Contains(graph.Edges, e => e.FromId == conditionId && e.ToId == a);
        Assert.Contains(graph.Edges, e => e.FromId == conditionId && e.ToId == b);
        Assert.DoesNotContain(graph.Edges, e => e.FromId == a && e.ToId == b);
    }

    [Fact]
    public void Branch_edges_carry_their_branch_name()
    {
        var condition = Branching("Condition", "If",
            ("Yes", new[] { "A" }),
            ("No", new[] { "B" }));
        var graph = SolutionGraphBuilder.BuildFlow(Flow(condition));

        var labels = graph.Edges.Where(e => e.Label is not null).Select(e => e.Label).ToList();
        Assert.Contains("Yes", labels);
        Assert.Contains("No", labels);
    }

    [Fact]
    public void Siblings_that_run_after_the_same_action_share_a_layer()
    {
        // Two actions both running after "Start" are parallel, so they must not
        // be chained one to the other.
        var start = new FlowAction { Name = "Start", Type = "Compose" };
        var left = new FlowAction { Name = "Left", Type = "Compose" };
        left.RunAfter.Add("Start");
        var right = new FlowAction { Name = "Right", Type = "Compose" };
        right.RunAfter.Add("Start");

        var graph = SolutionGraphBuilder.BuildFlow(Flow(start, left, right));
        var startId = graph.Nodes.Single(n => n.Title == "Start").Id;

        Assert.Equal(2, graph.Edges.Count(e => e.FromId == startId));
        Assert.DoesNotContain(graph.Edges, e =>
            e.FromId == graph.Nodes.Single(n => n.Title == "Left").Id &&
            e.ToId == graph.Nodes.Single(n => n.Title == "Right").Id);

        DiagramRenderer.Layout(graph);
        var leftNode = graph.Nodes.Single(n => n.Title == "Left");
        var rightNode = graph.Nodes.Single(n => n.Title == "Right");
        // Side by side: same row, different columns.
        Assert.Equal(leftNode.Y, rightNode.Y, 1);
        Assert.NotEqual(leftNode.X, rightNode.X);
    }

    [Fact]
    public void A_cluster_box_encloses_every_node_inside_it()
    {
        var scope = Branching("Try", "Scope", (string.Empty, new[] { "First", "Second" }));
        var graph = SolutionGraphBuilder.BuildFlow(Flow(scope));
        DiagramRenderer.Layout(graph);

        var cluster = Assert.Single(graph.Clusters);
        Assert.True(cluster.HasBounds);
        foreach (var node in graph.Nodes.Where(n => n.ClusterId == cluster.Id))
        {
            Assert.True(node.Left >= cluster.Left, $"{node.Title} escapes the left edge");
            Assert.True(node.Right <= cluster.Right, $"{node.Title} escapes the right edge");
            Assert.True(node.Top >= cluster.Top, $"{node.Title} escapes the top edge");
            Assert.True(node.Bottom <= cluster.Bottom, $"{node.Title} escapes the bottom edge");
        }
    }

    [Fact]
    public void Corner_rounding_never_exceeds_half_the_shortest_segment()
    {
        // Two edges leaving one point and turning opposite ways used to bow into
        // a lens, because each corner rounded by half its segment length.
        var points = new List<PointD>
        {
            new(0, 0), new(0, 6), new(60, 6), new(60, 100),
        };
        var steps = EdgeGeometry.Rounded(points, radius: 40);

        // Every produced point must stay within the polyline's bounding box.
        foreach (var step in steps)
        {
            Assert.InRange(step.To.X, 0, 60);
            Assert.InRange(step.To.Y, 0, 100);
        }
        Assert.Equal(points[0], steps[0].To);
        Assert.Equal(points[^1], steps[^1].To);
    }
}
