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
    /// One cloud flow as a flowchart: a stadium for the trigger, a rounded box
    /// per action, a diamond for a condition, with nested actions branching off
    /// their parent. This is a real PowerDocu capability Shieldsmith previously lacked.
    /// </summary>
    public static Graph BuildFlow(ProcessModel process)
    {
        var graph = new Graph
        {
            Direction = LayoutDirection.TopToBottom,
            Title = process.Name,
        };

        var triggerId = "__trigger";
        var trigger = graph.AddNode(triggerId, process.CloudFlow?.Trigger?.Summary ?? "Trigger");
        trigger.Shape = NodeShape.Stadium;
        trigger.Subtitle = process.CloudFlow?.Trigger?.Connector;

        if (process.CloudFlow is null) return graph;

        var counter = 0;
        AddActions(graph, process.CloudFlow.Actions, triggerId, ref counter);
        return graph;
    }

    private static void AddActions(Graph graph, IReadOnlyList<FlowAction> actions,
        string parentId, ref int counter)
    {
        var previousId = parentId;
        foreach (var action in actions)
        {
            var id = $"n{counter++}";
            var node = graph.AddNode(id, action.DisplayName);
            node.Shape = IsCondition(action) ? NodeShape.Diamond : NodeShape.RoundedBox;
            node.Subtitle = string.IsNullOrEmpty(action.OperationId) ? action.Type : action.OperationId;

            graph.AddEdge(previousId, id);

            if (action.Children.Count > 0)
            {
                // Children hang off this action; control then continues from the
                // last child so the chart reads as one path rather than a fork
                // that never rejoins.
                var branchCounter = counter;
                AddActions(graph, action.Children, id, ref branchCounter);
                counter = branchCounter;
                previousId = LastDescendantId(graph, id);
            }
            else
            {
                previousId = id;
            }
        }
    }

    private static string LastDescendantId(Graph graph, string fromId)
    {
        var current = fromId;
        var guard = 0;
        while (guard++ < 500)
        {
            var next = graph.Edges.LastOrDefault(e =>
                string.Equals(e.FromId, current, StringComparison.OrdinalIgnoreCase));
            if (next is null) return current;
            current = next.ToId;
        }
        return current;
    }

    private static bool IsCondition(FlowAction action) =>
        action.Type.Equals("If", StringComparison.OrdinalIgnoreCase)
        || action.Type.Equals("Switch", StringComparison.OrdinalIgnoreCase);

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
