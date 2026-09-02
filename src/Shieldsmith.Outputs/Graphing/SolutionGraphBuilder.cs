using Shieldsmith.Core.Models;
using Shieldsmith.Diagrams;

namespace Shieldsmith.Outputs.Graphing;

/// <summary>
/// Translates a parsed solution into graphs for Shieldsmith's own layout engine:
/// an entity relationship diagram, and a flowchart per cloud flow.
/// </summary>
public static class SolutionGraphBuilder
{
    public static Graph BuildErd(SolutionModel model, bool showColumns = true)
    {
        var graph = new Graph
        {
            Direction = LayoutDirection.TopToBottom,
            Title = $"{model.DisplayName} data model",
        };

        var drawable = model.Entities
            .Where(e => e.Attributes.Count > 0 || HasRelationship(model, e))
            .ToList();

        foreach (var entity in drawable)
        {
            var node = graph.AddNode(entity.SchemaName,
                string.IsNullOrEmpty(entity.DisplayName) ? entity.SchemaName : entity.DisplayName);
            node.Subtitle = entity.LogicalName;
            node.Shape = NodeShape.Record;

            if (!showColumns) continue;
            foreach (var attribute in entity.Attributes)
            {
                node.Lines.Add(new NodeLine
                {
                    Text = $"{Display(attribute)}: {attribute.TypeDisplay}{(attribute.IsRequired ? " *" : string.Empty)}",
                    Marker = attribute.IsPrimaryId ? "PK" : attribute.IsPrimaryName ? "N" : null,
                    Emphasise = attribute.IsPrimaryId || attribute.IsPrimaryName,
                });
            }
        }

        foreach (var relationship in model.OneToManyRelationships)
        {
            var many = Resolve(drawable, relationship.ReferencingEntity);
            var one = Resolve(drawable, relationship.ReferencedEntity);
            if (many is null || one is null) continue;

            // The many side points at the one side, crow's foot on the many end.
            var edge = graph.AddEdge(one.SchemaName, many.SchemaName);
            edge.Label = relationship.ReferencingAttribute;
            edge.FromEnding = EdgeEnding.None;
            edge.ToEnding = EdgeEnding.CrowsFoot;
        }

        foreach (var relationship in model.ManyToManyRelationships)
        {
            var first = Resolve(drawable, relationship.Entity1);
            var second = Resolve(drawable, relationship.Entity2);
            if (first is null || second is null) continue;

            var edge = graph.AddEdge(first.SchemaName, second.SchemaName);
            edge.Label = relationship.IntersectEntity;
            edge.Dashed = true;
            edge.FromEnding = EdgeEnding.CrowsFoot;
            edge.ToEnding = EdgeEnding.CrowsFoot;
        }

        return graph;
    }

    /// <summary>
    /// One cloud flow as a flowchart with the shape of the flow itself: a
    /// stadium for the trigger, a rounded box per action, a diamond for a
    /// condition, a drawn box around every scope and loop, and branches drawn
    /// side by side rather than stacked.
    ///
    /// Two things make that work. Order comes from <c>runAfter</c> rather than
    /// document order, so actions that genuinely run in parallel land in the
    /// same layer and sit beside each other. And a container's children stay in
    /// a cluster, so a Catch scope is a box around its contents rather than a
    /// run of boxes indistinguishable from the rest of the flow.
    /// </summary>
    public static Graph BuildFlow(ProcessModel process)
    {
        var graph = new Graph
        {
            Direction = LayoutDirection.TopToBottom,
            Title = process.Name,
        };

        var trigger = graph.AddNode(TriggerId, process.CloudFlow?.Trigger?.Summary ?? "Trigger");
        trigger.Shape = NodeShape.Stadium;
        trigger.Subtitle = process.CloudFlow?.Trigger?.Connector;

        if (process.CloudFlow is null) return graph;

        var counter = 0;
        AddSequence(graph, process.CloudFlow.Actions, new[] { TriggerId }, null, ref counter);
        return graph;
    }

    private const string TriggerId = "__trigger";

    /// <summary>
    /// Lays out one list of sibling actions, honouring runAfter, and returns the
    /// ids that anything following this sequence should run after.
    /// </summary>
    private static List<string> AddSequence(Graph graph, IReadOnlyList<FlowAction> actions,
        IReadOnlyList<string> entryIds, string? clusterId, ref int counter)
    {
        if (actions.Count == 0) return entryIds.ToList();

        // Every sibling's node id, so runAfter names can be resolved to nodes.
        var idByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exitsByName = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var order = new List<FlowAction>();
        var previousExits = entryIds.ToList();

        foreach (var action in actions)
        {
            var id = $"n{counter++}";
            idByName[action.Name] = id;
            order.Add(action);
        }

        foreach (var action in order)
        {
            var id = idByName[action.Name];
            var node = graph.AddNode(id, action.DisplayName);
            node.Shape = ShapeFor(action);
            node.Subtitle = string.IsNullOrEmpty(action.OperationId) ? action.Type : action.OperationId;
            node.ClusterId = clusterId;

            // A named predecessor that is a sibling wires to that sibling's
            // exits. With no usable runAfter, follow the previous sibling rather
            // than the sequence entry: only the first action in a list starts
            // from the entry, and treating every unnamed action as starting
            // there would draw a sequential list as a parallel fan.
            var predecessors = new List<string>();
            foreach (var name in action.RunAfter)
                if (exitsByName.TryGetValue(name, out var exits)) predecessors.AddRange(exits);
                else if (idByName.TryGetValue(name, out var sibling)) predecessors.Add(sibling);
            if (predecessors.Count == 0) predecessors.AddRange(previousExits);

            foreach (var from in predecessors.Distinct())
                graph.AddEdge(from, id);

            exitsByName[action.Name] = action.Branches.Count > 0
                ? AddContainer(graph, action, id, clusterId, ref counter)
                : new List<string> { id };
            previousExits = exitsByName[action.Name];
        }

        // Anything after this sequence runs after whatever nothing else ran
        // after: the leaves. A flow that fans out and never rejoins has several.
        var consumed = order
            .SelectMany(a => a.RunAfter)
            .Where(idByName.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leaves = order.Where(a => !consumed.Contains(a.Name))
            .SelectMany(a => exitsByName[a.Name])
            .Distinct()
            .ToList();
        return leaves.Count > 0 ? leaves : entryIds.ToList();
    }

    /// <summary>
    /// Draws a container action's branches inside a cluster and returns the ids
    /// control continues from. Each branch is laid out independently, which is
    /// what puts two paths beside each other rather than one after the other.
    /// </summary>
    private static List<string> AddContainer(Graph graph, FlowAction action, string headerId,
        string? parentClusterId, ref int counter)
    {
        var clusterId = $"c{headerId}";
        var cluster = graph.AddCluster(clusterId, action.DisplayName, parentClusterId);
        cluster.Subtitle = action.Type;

        var exits = new List<string>();
        foreach (var branch in action.Branches)
        {
            // A labelled branch gets its own nested box so "Yes" and "No" read
            // as separate paths; an unlabelled scope body does not need one.
            var branchClusterId = clusterId;
            if (branch.Label.Length > 0 && action.Branches.Count > 1)
            {
                branchClusterId = $"{clusterId}_{Identifier(branch.Label)}";
                graph.AddCluster(branchClusterId, branch.Label, clusterId);
            }

            var branchExits = AddSequence(graph, branch.Actions, new[] { headerId },
                branchClusterId, ref counter);
            exits.AddRange(branchExits);

            // Label the edge into the branch with the branch name, so a reader
            // can tell which path is which without tracing boxes.
            if (branch.Label.Length > 0 && branch.Actions.Count > 0)
            {
                var first = graph.Edges.FirstOrDefault(e =>
                    e.FromId == headerId && e.Label is null &&
                    graph.Find(e.ToId)?.ClusterId == branchClusterId);
                if (first is not null) first.Label = branch.Label;
            }
        }

        return exits.Count > 0 ? exits : new List<string> { headerId };
    }

    private static NodeShape ShapeFor(FlowAction action) => action.Type switch
    {
        "If" or "Switch" => NodeShape.Diamond,
        _ => NodeShape.RoundedBox,
    };

    private static string Identifier(string text) =>
        new string(text.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static bool HasRelationship(SolutionModel model, EntityModel entity) =>
        model.OneToManyRelationships.Any(r =>
            Matches(r.ReferencedEntity, entity) || Matches(r.ReferencingEntity, entity)) ||
        model.ManyToManyRelationships.Any(r =>
            Matches(r.Entity1, entity) || Matches(r.Entity2, entity));

    private static EntityModel? Resolve(IEnumerable<EntityModel> entities, string name) =>
        entities.FirstOrDefault(e => Matches(name, e));

    private static bool Matches(string name, EntityModel entity) =>
        string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);

    private static string Display(AttributeModel attribute) =>
        string.IsNullOrEmpty(attribute.DisplayName) ? attribute.LogicalName : attribute.DisplayName;
}
