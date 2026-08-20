using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

/// <summary>
/// Covers the three component types that close the last PowerDocu parity gaps:
/// canvas apps, Copilot Studio agents and desktop flows.
/// </summary>
public sealed class CanvasAgentDesktopTests : IDisposable
{
    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;

    public CanvasAgentDesktopTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        _unpacked = SolutionUnpacker.Unpack(Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip"));
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    // ---- Canvas apps ------------------------------------------------------

    [Fact]
    public void Canvas_app_metadata_and_internals_are_read()
    {
        var app = Assert.Single(_model.CanvasApps);
        Assert.Equal("Trip Planner", app.DisplayName);
        Assert.Equal("ct_tripplanner_a1b2c", app.Name);
        Assert.Equal("Canvas app", app.KindDisplay);
        Assert.False(app.IsComponentLibrary);
        Assert.True(app.HasInternals);
        Assert.Equal("DesktopOrTablet", app.FormFactor);
        Assert.Equal("1.346", app.DocumentVersion);
        // LastSavedDateTimeUTC is US-formatted with no timezone suffix.
        Assert.Equal(new DateTime(2026, 3, 14, 9, 21, 7, DateTimeKind.Utc), app.LastSavedUtc);
    }

    [Fact]
    public void Msapp_entries_with_backslash_separators_are_read()
    {
        // The fixture stores entries as References\DataSources.json, which real
        // exports do; .NET does not normalise those names.
        var app = Assert.Single(_model.CanvasApps);
        Assert.NotEmpty(app.DataSources);
        Assert.NotEmpty(app.Screens);
    }

    [Fact]
    public void Only_dataverse_tables_survive_the_data_source_filter()
    {
        var app = Assert.Single(_model.CanvasApps);
        // The fixture has five data sources; only two are real tables.
        Assert.Equal(2, app.DataSources.Count);
        Assert.All(app.DataSources, d => Assert.True(d.IsTable));
        Assert.Contains(app.DataSources, d => d.LogicalName == "ct_trip");
        Assert.DoesNotContain(app.DataSources, d => d.Name.Contains("_views"));
        Assert.DoesNotContain(app.DataSources, d => d.Name == "CustomGallerySample");
    }

    [Fact]
    public void Screens_exclude_the_app_object_and_keep_their_order()
    {
        var app = Assert.Single(_model.CanvasApps);
        Assert.Equal(2, app.Screens.Count);
        Assert.Equal("Home Screen", app.Screens[0].Name);
        Assert.Equal("Trip Detail", app.Screens[1].Name);
        // Controls/1.json is the App object, not a screen.
        Assert.DoesNotContain(app.Screens, s => s.Name == "App");
    }

    [Fact]
    public void Control_hierarchy_and_authored_formulas_are_captured()
    {
        var home = _model.CanvasApps[0].Screens.First(s => s.Name == "Home Screen");

        var container = home.Controls.First(c => c.Name == "HeaderContainer");
        // A group container is typed by its variant, not the shared template.
        Assert.Equal("horizontalAutoLayoutContainer", container.Type);
        Assert.Single(container.Children);
        Assert.Equal("TitleLabel", container.Children[0].Name);

        var button = home.AllControls().First(c => c.Name == "NewTripButton");
        Assert.Equal("button", button.Type);
        Assert.Contains("OnSelect", button.Properties.Keys);
        Assert.Contains("Navigate('Trip Detail')", button.Properties["OnSelect"]);
    }

    [Fact]
    public void Variables_come_from_the_app_object_not_only_screens()
    {
        var app = Assert.Single(_model.CanvasApps);
        // varCurrentUser and varIsAdmin are declared in the App object's OnStart,
        // which is not a screen; varSelectedTrip is set on a screen.
        Assert.Contains("varCurrentUser", app.GlobalVariables);
        Assert.Contains("varIsAdmin", app.GlobalVariables);
        Assert.Contains("varSelectedTrip", app.GlobalVariables);
        Assert.Contains("colRecentTrips", app.Collections);
        Assert.Contains("colPendingSaves", app.Collections);
    }

    // ---- Copilot Studio agents -------------------------------------------

    [Fact]
    public void Agent_is_found_by_its_bots_folder_not_by_solution_xml()
    {
        // Real exports do not declare agents in RootComponents at all.
        Assert.DoesNotContain(_model.RootComponents,
            c => c.DisplayIdentifier.Contains("travelAssistant", StringComparison.OrdinalIgnoreCase));

        var agent = Assert.Single(_model.Agents);
        Assert.Equal("Travel Assistant", agent.Name);
        Assert.Equal("ct_travelAssistant", agent.SchemaName);
        Assert.Equal("Authenticate manually", agent.AuthenticationModeDisplay);
        Assert.True(agent.GenerativeActionsEnabled);
    }

    [Fact]
    public void Agent_topic_carries_trigger_phrases_and_message_variations()
    {
        var topic = Assert.Single(_model.Agents[0].Topics);
        Assert.Equal("Greeting", topic.DisplayName);
        Assert.Contains("Hello", topic.TriggerQueries);
        Assert.Contains("Can you help me book a trip", topic.TriggerQueries);
        // activity.text is a sequence, not a scalar.
        Assert.Contains("Hello, I can help you plan and book a trip.", topic.Messages);
    }

    [Fact]
    public void Agent_instructions_come_from_the_agent_component()
    {
        var agent = Assert.Single(_model.Agents);
        Assert.Contains("Help the traveller choose a destination", agent.Instructions);
        // The block scalar spans lines and both survive.
        Assert.Contains("confirm dates", agent.Instructions);

        var tool = Assert.Single(agent.Tools);
        Assert.Equal("AgentDialog", tool.Kind);
    }

    [Fact]
    public void Agent_knowledge_source_is_read()
    {
        var source = Assert.Single(_model.Agents[0].KnowledgeSources);
        Assert.Equal("PublicSiteSearchSource", source.SourceKind);
        Assert.Equal("https://www.gov.uk/foreign-travel-advice", source.Site);
    }

    // ---- Desktop flows ----------------------------------------------------

    [Fact]
    public void Desktop_flow_is_identified_by_category_six()
    {
        var process = Assert.Single(_model.Processes, p => p.Kind == ProcessKind.DesktopFlow);
        Assert.Equal("Export bookings to Excel", process.Name);
        Assert.Equal(2, process.UiFlowType); // Power Automate Desktop
        Assert.NotNull(process.DesktopFlow);
        Assert.Equal("2022.07", process.DesktopFlow!.SchemaVersion);
    }

    [Fact]
    public void Robin_script_inputs_outputs_and_subflows_are_parsed()
    {
        var detail = _model.Processes.First(p => p.Kind == ProcessKind.DesktopFlow).DesktopFlow!;

        var input = Assert.Single(detail.Inputs);
        Assert.Equal("ExcelPath", input.Name);
        Assert.Equal("String", input.Type);

        var output = Assert.Single(detail.Outputs);
        Assert.Equal("RowsExported", output.Name);

        // Main plus the Init function.
        Assert.Equal(2, detail.Subflows.Count);
        var init = detail.Subflows.First(s => s.Name == "Init");
        Assert.True(init.IsGlobal);
    }

    [Fact]
    public void Robin_action_steps_carry_module_depth_and_output_bindings()
    {
        var detail = _model.Processes.First(p => p.Kind == ProcessKind.DesktopFlow).DesktopFlow!;
        var main = detail.Subflows.First(s => s.Name == "Main");

        var launch = main.Steps.First(s => s.Action == "Excel.LaunchExcel.LaunchAndOpen");
        Assert.Equal("Excel", launch.Module);
        Assert.Equal(0, launch.Depth);
        Assert.Contains("ExcelInstance", launch.OutputVariables);

        // Steps inside LOOP FOREACH are one level deep.
        var write = main.Steps.First(s => s.Action.StartsWith("Excel.WriteToExcel"));
        Assert.Equal(1, write.Depth);

        // System and Flow are pseudo-modules and are excluded.
        Assert.Equal(new[] { "DateTime", "Excel", "Variables" }, detail.Modules);
    }
}
