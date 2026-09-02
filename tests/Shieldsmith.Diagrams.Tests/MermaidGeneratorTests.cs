using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Shieldsmith.Outputs.Mermaid;
using Xunit;

namespace Shieldsmith.Diagrams.Tests;

public sealed class MermaidGeneratorTests : IDisposable
{
    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;

    public MermaidGeneratorTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        _unpacked = SolutionUnpacker.Unpack(Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip"));
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    [Fact]
    public void Erd_uses_correct_mermaid_cardinality()
    {
        var mermaid = MermaidGenerator.BuildErd(_model);

        Assert.StartsWith("erDiagram", mermaid);
        // One Destination to many Trip.
        Assert.Contains("ct_Destination ||--o{ ct_Trip", mermaid);
        // One Trip to many Booking.
        Assert.Contains("ct_Trip ||--o{ ct_Booking", mermaid);
        // Trip to Traveller is many-to-many.
        Assert.Contains("}o--o{", mermaid);
        // Columns are declared with a type token and the key marker.
        Assert.Contains("primarykey ct_tripid PK", mermaid);
    }

    [Fact]
    public void Erd_identifiers_are_mermaid_safe()
    {
        var model = new SolutionModel();
        var entity = new EntityModel { SchemaName = "weird name!", LogicalName = "weird name!" };
        entity.Attributes.Add(new AttributeModel
        {
            LogicalName = "col with space",
            Type = "text field",
        });
        model.Entities.Add(entity);
        model.OneToManyRelationships.Add(new OneToManyRelationship
        {
            SchemaName = "r", ReferencedEntity = "weird name!", ReferencingEntity = "weird name!",
        });

        var mermaid = MermaidGenerator.BuildErd(model);

        // Spaces and punctuation would break Mermaid's parser.
        Assert.Contains("weird_name_", mermaid);
        Assert.Contains("text_field col_with_space", mermaid);
        Assert.DoesNotContain("weird name!", mermaid);
    }

    [Fact]
    public void Flowchart_carries_the_trigger_and_every_action()
    {
        var process = _model.Processes.First(p => p.Name == "Send booking confirmation");
        var mermaid = MermaidGenerator.BuildFlowchart(process);

        Assert.StartsWith("flowchart TD", mermaid);
        Assert.Contains("trigger([\"When a ct_booking row is updated\"])", mermaid);
        Assert.Contains("Get trip", mermaid);
        Assert.Contains("Send confirmation email", mermaid);
        Assert.Contains("trigger --> n0", mermaid);
        Assert.Contains("n0 --> n1", mermaid);
    }

    [Fact]
    public void Flowchart_quotes_are_neutralised()
    {
        var process = new ProcessModel { Name = "Quoted", Category = 5 };
        process.CloudFlow = new CloudFlowDetail
        {
            Trigger = new FlowTrigger { Name = "t", Type = "Request", Kind = "Button" },
        };
        process.CloudFlow.Actions.Add(new FlowAction
        {
            Name = "Say_\"hello\"",
            Type = "Compose",
        });

        var mermaid = MermaidGenerator.BuildFlowchart(process);

        // A raw double quote inside a Mermaid label terminates the label early.
        Assert.DoesNotContain("\"hello\"", mermaid);
        Assert.Contains("'hello'", mermaid);
    }

    [Fact]
    public void Nested_actions_become_a_connected_chain()
    {
        var process = new ProcessModel { Name = "Nested", Category = 5 };
        var scope = new FlowAction { Name = "Scope", Type = "Scope" };
        // Nested actions live in branches now; Children is the flattened view.
        var body = new FlowBranch();
        body.Actions.Add(new FlowAction { Name = "Inner", Type = "Compose" });
        scope.Branches.Add(body);
        process.CloudFlow = new CloudFlowDetail
        {
            Trigger = new FlowTrigger { Name = "t", Type = "Request" },
        };
        process.CloudFlow.Actions.Add(scope);
        process.CloudFlow.Actions.Add(new FlowAction { Name = "After", Type = "Compose" });

        var mermaid = MermaidGenerator.BuildFlowchart(process);

        Assert.Contains("Scope", mermaid);
        Assert.Contains("Inner", mermaid);
        Assert.Contains("After", mermaid);
        // The chain continues from the nested child, not from the scope itself.
        Assert.Contains("n1 --> n2", mermaid);
    }
}
