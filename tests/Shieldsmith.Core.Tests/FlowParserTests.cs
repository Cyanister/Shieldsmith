using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

public sealed class FlowParserTests : IDisposable
{
    private static string SampleZipPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip");
        }
    }

    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;

    public FlowParserTests()
    {
        _unpacked = SolutionUnpacker.Unpack(SampleZipPath);
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    [Fact]
    public void All_processes_are_read_with_their_kinds()
    {
        // Two cloud flows, one classic workflow, one desktop flow.
        Assert.Equal(4, _model.Processes.Count);
        Assert.Equal(2, _model.Processes.Count(p => p.Kind == ProcessKind.CloudFlow));
        Assert.Equal(1, _model.Processes.Count(p => p.Kind == ProcessKind.DesktopFlow));
        var classic = Assert.Single(_model.Processes, p => p.Kind == ProcessKind.ClassicWorkflow);
        Assert.Equal("Trip approval", classic.Name);
        Assert.Equal("ct_trip", classic.PrimaryEntity);
    }

    [Fact]
    public void Cloud_flow_trigger_is_decoded()
    {
        var flow = _model.Processes.First(p => p.Name == "Send booking confirmation");
        Assert.NotNull(flow.CloudFlow);
        var trigger = flow.CloudFlow!.Trigger;
        Assert.NotNull(trigger);
        Assert.Equal("ct_booking", trigger!.EntityName);
        Assert.Equal("updated", trigger.Message);
        Assert.Contains("ct_booking", trigger.Summary);
    }

    [Fact]
    public void Cloud_flow_actions_are_ordered_by_run_after()
    {
        var flow = _model.Processes.First(p => p.Name == "Send booking confirmation");
        var actions = flow.CloudFlow!.Actions;
        Assert.Equal(2, actions.Count);
        // Send_confirmation_email runs after Get_trip, so Get_trip comes first.
        Assert.Equal("Get_trip", actions[0].Name);
        Assert.Equal("Send_confirmation_email", actions[1].Name);
        Assert.Equal("GetItem", actions[0].OperationId);
        Assert.Equal("shared_commondataserviceforapps", actions[0].Connector);
        Assert.Equal("SendEmailV2", actions[1].OperationId);
        Assert.Equal("shared_office365", actions[1].Connector);
    }

    [Fact]
    public void Cloud_flow_connectors_are_listed()
    {
        var flow = _model.Processes.First(p => p.Name == "Send booking confirmation");
        var connectors = flow.CloudFlow!.ConnectorsUsed.ToList();
        Assert.Contains("shared_commondataserviceforapps", connectors);
        Assert.Contains("shared_office365", connectors);
    }

    [Fact]
    public void Root_component_guids_resolve_to_process_and_web_resource_names()
    {
        var flowComponents = _model.RootComponents.Where(c => c.TypeCode == 29).ToList();
        Assert.All(flowComponents, c => Assert.NotEqual(string.Empty, c.ResolvedName));
        Assert.Contains(flowComponents, c => c.DisplayIdentifier == "Send booking confirmation");
        Assert.Contains(flowComponents, c => c.DisplayIdentifier == "Trip approval");
    }

    [Fact]
    public void Web_resources_are_read()
    {
        var resource = Assert.Single(_model.WebResources);
        Assert.Equal("ct_tripFormScript", resource.Name);
        Assert.Equal("JavaScript", resource.TypeName);
    }

    [Fact]
    public void Nested_actions_parse_into_children()
    {
        const string clientData = """
        {
          "properties": {
            "definition": {
              "triggers": { "Recurrence1": { "type": "Recurrence", "recurrence": { "frequency": "Day", "interval": "1" } } },
              "actions": {
                "Scope1": {
                  "type": "Scope",
                  "runAfter": {},
                  "actions": {
                    "Condition1": {
                      "type": "If",
                      "runAfter": {},
                      "actions": { "Inner_yes": { "type": "Compose", "runAfter": {} } },
                      "else": { "actions": { "Inner_no": { "type": "Compose", "runAfter": {} } } }
                    }
                  }
                }
              }
            }
          }
        }
        """;

        var detail = FlowParser.ParseClientData(clientData);
        Assert.Equal("On a schedule: every 1 day", detail.Trigger!.Summary);
        var scope = Assert.Single(detail.Actions);
        var condition = Assert.Single(scope.Children);
        Assert.Equal(2, condition.Children.Count);
        Assert.Equal(4, detail.AllActions().Count());
    }
}
