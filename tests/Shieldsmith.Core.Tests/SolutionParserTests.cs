using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

public sealed class SolutionParserTests : IDisposable
{
    private static string SampleZipPath
    {
        get
        {
            // Walk up from the test bin directory to the repo root.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip");
        }
    }

    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;

    public SolutionParserTests()
    {
        _unpacked = SolutionUnpacker.Unpack(SampleZipPath);
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    [Fact]
    public void Manifest_is_read_correctly()
    {
        Assert.Equal("ContosoTravel", _model.UniqueName);
        Assert.Equal("Contoso Travel", _model.DisplayName);
        Assert.Equal("1.0.0.7", _model.Version);
        Assert.False(_model.IsManaged);
        Assert.Equal("ContosoTravelLtd", _model.PublisherUniqueName);
        Assert.Equal("Contoso Travel Ltd", _model.PublisherDisplayName);
        Assert.Equal("ct", _model.CustomizationPrefix);
    }

    [Fact]
    public void Root_components_carry_schema_names_and_ids()
    {
        Assert.Equal(17, _model.RootComponents.Count);
        var entityComponents = _model.RootComponents.Where(c => c.TypeCode == 1).ToList();
        Assert.Equal(4, entityComponents.Count);
        Assert.All(entityComponents, c => Assert.NotEqual(string.Empty, c.SchemaName));

        // Type 29 covers every process kind: cloud flows, classic workflows and
        // desktop flows all share it, and are told apart by Category.
        var flowComponents = _model.RootComponents.Where(c => c.TypeCode == 29).ToList();
        Assert.Equal(4, flowComponents.Count);
        Assert.All(flowComponents, c => Assert.NotEqual(string.Empty, c.Id));
        // Braces are stripped from ids.
        Assert.DoesNotContain(flowComponents, c => c.Id.Contains('{'));

        Assert.Equal("Environment variable definition",
            _model.RootComponents.First(c => c.TypeCode == 380).TypeName);
    }

    [Fact]
    public void Entities_are_scoped_and_complete()
    {
        // Exactly the four declared entities: no phantom entities from nested elements.
        Assert.Equal(4, _model.Entities.Count);

        var trip = _model.FindEntity("ct_Trip");
        Assert.NotNull(trip);
        Assert.Equal("Trip", trip!.DisplayName);
        Assert.Equal("ct_trip", trip.LogicalName);
        Assert.Equal("A planned journey with a destination, bookings and travellers.", trip.Description);
        Assert.Equal("UserOwned", trip.OwnershipType);
        Assert.True(trip.IsAuditEnabled);
        Assert.Equal(7, trip.Attributes.Count);
    }

    [Fact]
    public void Primary_id_and_primary_name_are_identified()
    {
        var trip = _model.FindEntity("ct_Trip")!;
        Assert.Equal("ct_tripid", trip.PrimaryIdAttribute?.LogicalName);
        Assert.Equal("ct_name", trip.PrimaryNameAttribute?.LogicalName);
        Assert.Equal("Trip Name", trip.PrimaryNameAttribute?.DisplayName);
    }

    [Fact]
    public void Required_levels_parse_the_real_tokens()
    {
        var trip = _model.FindEntity("ct_Trip")!;
        Assert.Equal(RequiredLevel.SystemRequired, trip.Attributes.First(a => a.LogicalName == "ct_tripid").RequiredLevel);
        Assert.Equal(RequiredLevel.ApplicationRequired, trip.Attributes.First(a => a.LogicalName == "ct_startdate").RequiredLevel);
        Assert.Equal(RequiredLevel.Recommended, trip.Attributes.First(a => a.LogicalName == "ct_budget").RequiredLevel);
        Assert.Equal(RequiredLevel.None, trip.Attributes.First(a => a.LogicalName == "ct_enddate").RequiredLevel);

        Assert.True(trip.Attributes.First(a => a.LogicalName == "ct_startdate").IsRequired);
        Assert.False(trip.Attributes.First(a => a.LogicalName == "ct_budget").IsRequired);
    }

    [Fact]
    public void Relationships_are_read_from_the_solution_level_block()
    {
        Assert.Equal(2, _model.OneToManyRelationships.Count);

        var destinationTrip = _model.OneToManyRelationships.First(r => r.SchemaName == "ct_destination_trip");
        Assert.Equal("ct_Destination", destinationTrip.ReferencedEntity);
        Assert.Equal("ct_Trip", destinationTrip.ReferencingEntity);
        Assert.Equal("ct_destinationid", destinationTrip.ReferencingAttribute);
        Assert.Equal("RemoveLink", destinationTrip.CascadeDelete);

        var single = Assert.Single(_model.ManyToManyRelationships);
        Assert.Equal("ct_Trip", single.Entity1);
        Assert.Equal("ct_Traveller", single.Entity2);
        Assert.Equal("ct_trip_traveller", single.IntersectEntity);
    }

    [Fact]
    public void Lookup_attributes_without_declared_relationships_are_inferred_only()
    {
        // ownerid on Trip and Traveller have no declared relationship: inferred.
        Assert.Equal(2, _model.InferredLookups.Count);
        Assert.All(_model.InferredLookups, l => Assert.Equal("ownerid", l.AttributeLogicalName));

        // ct_destinationid and ct_tripid have declared relationships: not duplicated as inferred.
        Assert.DoesNotContain(_model.InferredLookups, l => l.AttributeLogicalName == "ct_destinationid");
        Assert.DoesNotContain(_model.InferredLookups, l => l.AttributeLogicalName == "ct_tripid");
    }

    [Fact]
    public void Environment_variables_join_definitions_and_values()
    {
        Assert.Equal(2, _model.EnvironmentVariables.Count);

        var apiUrl = _model.EnvironmentVariables.First(v => v.SchemaName == "ct_ApiBaseUrl");
        Assert.Equal("API Base URL", apiUrl.DisplayName);
        Assert.Equal("String", apiUrl.TypeName);
        Assert.True(apiUrl.IsRequired);
        Assert.Equal("https://api.contoso.example/travel", apiUrl.DefaultValue);
        Assert.False(apiUrl.HasCurrentValue);

        var sendEmails = _model.EnvironmentVariables.First(v => v.SchemaName == "ct_SendEmails");
        Assert.Equal("Boolean", sendEmails.TypeName);
        Assert.True(sendEmails.HasCurrentValue);
        Assert.Equal("yes", sendEmails.CurrentValue);
    }

    [Fact]
    public void Alternate_keys_are_read()
    {
        var traveller = _model.FindEntity("ct_Traveller")!;
        var key = Assert.Single(traveller.Keys);
        Assert.Equal("ct_email", key.LogicalName);
        Assert.Equal("ct_email", Assert.Single(key.KeyAttributes));
    }
}
