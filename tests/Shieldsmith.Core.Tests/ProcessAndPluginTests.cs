using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

/// <summary>
/// Business process flow stages and plugin registrations: the two component
/// types PowerDocu does not document at all.
/// </summary>
public sealed class ProcessAndPluginTests : IDisposable
{
    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;

    public ProcessAndPluginTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        _unpacked = SolutionUnpacker.Unpack(Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip"));
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    // ---- Business process flows -------------------------------------------

    [Fact]
    public void Business_process_flow_stages_are_read_in_order()
    {
        var process = Assert.Single(_model.Processes, p => p.Kind == ProcessKind.BusinessProcessFlow);
        Assert.Equal("Trip booking process", process.Name);
        Assert.Equal("ct_trip", process.PrimaryEntity);

        var detail = process.BusinessProcessFlow;
        Assert.NotNull(detail);
        Assert.Equal(new[] { "Enquiry", "Quoted", "Booked" },
            detail!.Stages.Select(s => s.Name).ToArray());
        Assert.Equal(new[] { 1, 2, 3 }, detail.Stages.Select(s => s.Order).ToArray());
        Assert.Equal(5, detail.StepCount);
    }

    [Fact]
    public void Business_process_steps_carry_the_column_they_write()
    {
        var detail = _model.Processes.First(p => p.BusinessProcessFlow is not null).BusinessProcessFlow!;

        var enquiry = detail.Stages.First(s => s.Name == "Enquiry");
        Assert.Equal(2, enquiry.Steps.Count);
        Assert.Equal("Destination", enquiry.Steps[0].Name);
        Assert.Equal("ct_destinationid", enquiry.Steps[0].DataField);
        Assert.False(enquiry.Steps[0].IsSystemControl);

        // Platform-added controls are flagged so a reader can tell them from
        // steps a maker chose to put on the stage.
        var booked = detail.Stages.First(s => s.Name == "Booked");
        Assert.Contains(booked.Steps, s => s.IsSystemControl && s.DataField == "stageid");
    }

    [Fact]
    public void The_generated_stage_prefix_is_stripped_but_real_colons_survive()
    {
        Assert.Equal("Under Preparation",
            BusinessProcessFlowParser.StripStagePrefix("StageStep3: Under Preparation"));
        // A maker's own colon is not a generated prefix and must be kept.
        Assert.Equal("Review: legal",
            BusinessProcessFlowParser.StripStagePrefix("Review: legal"));
        Assert.Equal("Approved (Enter Start Date)",
            BusinessProcessFlowParser.StripStagePrefix("StageStep51: Approved (Enter Start Date)"));
    }

    // ---- Plugins -----------------------------------------------------------

    [Fact]
    public void Plugin_assembly_and_its_types_are_read()
    {
        var assembly = Assert.Single(_model.PluginAssemblies);
        Assert.Equal("ContosoTravel.Plugins", assembly.Name);
        Assert.Equal("1.2.0.0", assembly.Version);
        Assert.Equal("Sandbox", assembly.IsolationMode);
        Assert.Equal(2, assembly.Types.Count);
        Assert.Contains(assembly.Types, t => t.DisplayName == "BookingTotalPlugin");
    }

    [Fact]
    public void Steps_are_decoded_and_attached_to_the_assembly_that_implements_them()
    {
        var assembly = Assert.Single(_model.PluginAssemblies);
        // Two of the three registrations belong to this assembly; the third is
        // implemented by an assembly that ships in a different solution.
        Assert.Equal(2, assembly.Steps.Count);
        Assert.Equal(3, _model.SdkMessageSteps.Count);

        var booking = assembly.Steps.First(s => s.PrimaryEntity == "ct_booking");
        Assert.Equal("Update", booking.Message);
        Assert.Equal("Post-operation", booking.Stage);
        Assert.Equal("Synchronous", booking.Mode);
        Assert.Equal(new[] { "ct_cost", "ct_bookingtype" }, booking.FilteringAttributes.ToArray());
        Assert.Equal(1, booking.ImageCount);

        var trip = assembly.Steps.First(s => s.PrimaryEntity == "ct_trip");
        Assert.Equal("Create", trip.Message);
        Assert.Equal("Pre-operation", trip.Stage);
        Assert.Equal("Asynchronous", trip.Mode);
        Assert.Empty(trip.FilteringAttributes);
    }

    [Fact]
    public void A_renamed_step_reports_no_message_rather_than_a_guess()
    {
        // The export carries only a message GUID. The name is the only place
        // the message appears, so a maker who renamed the step has removed the
        // evidence, and inventing one would be worse than saying nothing.
        var renamed = _model.SdkMessageSteps.First(s => s.PrimaryEntity == "ct_traveller");
        Assert.Equal(string.Empty, renamed.Message);
        Assert.Equal("Pre-validation", renamed.Stage);
        Assert.DoesNotContain(_model.PluginAssemblies, a => a.Steps.Contains(renamed));
    }

    [Fact]
    public void The_message_is_only_trusted_when_the_table_it_names_matches()
    {
        Assert.Equal("Update", PluginParser.MessageFrom("X: Update of ct_trip", "ct_trip"));
        // Name says one table, registration says another: not trustworthy.
        Assert.Equal(string.Empty, PluginParser.MessageFrom("X: Update of ct_trip", "ct_booking"));
        Assert.Equal(string.Empty, PluginParser.MessageFrom("Nightly tidy-up", "ct_trip"));
    }
}
