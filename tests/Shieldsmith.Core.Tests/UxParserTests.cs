using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Core.Tests;

public sealed class UxParserTests : IDisposable
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

    public UxParserTests()
    {
        _unpacked = SolutionUnpacker.Unpack(SampleZipPath);
        _model = SolutionParser.Parse(_unpacked);
    }

    public void Dispose() => _unpacked.Dispose();

    [Fact]
    public void Forms_parse_with_tabs_sections_fields_and_libraries()
    {
        var trip = _model.FindEntity("ct_Trip")!;
        var form = Assert.Single(trip.Forms);
        Assert.Equal("Trip main form", form.Name);
        Assert.Equal("main", form.FormType);
        Assert.Equal("ct_tripFormScript", Assert.Single(form.Libraries));

        var tab = Assert.Single(form.Tabs);
        Assert.Equal("General", tab.Label);
        var section = Assert.Single(tab.Sections);
        Assert.Equal("Trip details", section.Label);
        Assert.Equal(new[] { "ct_name", "ct_startdate", "ct_destinationid" }, section.Fields);
    }

    [Fact]
    public void Views_parse_with_columns_from_layout_xml()
    {
        var trip = _model.FindEntity("ct_Trip")!;
        var view = Assert.Single(trip.Views);
        Assert.Equal("Active Trips", view.Name);
        Assert.True(view.IsDefault);
        Assert.Equal(new[] { "ct_name", "ct_startdate", "ct_budget" }, view.Columns);
    }

    [Fact]
    public void Option_sets_parse_with_values_and_labels()
    {
        var optionSet = Assert.Single(_model.OptionSets);
        Assert.Equal("ct_bookingtype", optionSet.Name);
        Assert.Equal("Booking Type", optionSet.DisplayName);
        Assert.True(optionSet.IsGlobal);
        Assert.Equal(3, optionSet.Options.Count);
        Assert.Equal("Flight", optionSet.Options[0].Label);
        Assert.Equal("100010000", optionSet.Options[0].Value);
    }

    [Fact]
    public void Security_roles_parse_with_privilege_levels()
    {
        var role = Assert.Single(_model.SecurityRoles);
        Assert.Equal("Travel Coordinator", role.Name);
        Assert.Equal(6, role.Privileges.Count);
        Assert.Equal(2, role.PrivilegeCountsByLevel["Global"]);
        Assert.Equal(2, role.PrivilegeCountsByLevel["Basic"]);
    }

    [Fact]
    public void App_modules_and_sitemap_parse_into_a_navigation_tree()
    {
        var app = Assert.Single(_model.AppModules);
        Assert.Equal("ct_TravelHub", app.UniqueName);
        Assert.Equal("Travel Hub", app.Name);
        Assert.Equal(3, app.ComponentCount);

        var siteMap = Assert.Single(_model.SiteMaps);
        var area = Assert.Single(siteMap.Areas);
        Assert.Equal("Travel", area.Title);
        Assert.Equal(2, area.Groups.Count);
        Assert.Equal("Planning", area.Groups[0].Title);
        Assert.Equal(2, area.Groups[0].SubAreas.Count);
        Assert.Equal("Trips", area.Groups[0].SubAreas[0].Title);
        Assert.Equal("ct_trip", area.Groups[0].SubAreas[0].Entity);
    }

    [Fact]
    public void Role_and_form_root_components_resolve_to_names()
    {
        var roleComponent = _model.RootComponents.First(c => c.TypeCode == 20);
        Assert.Equal("Travel Coordinator", roleComponent.DisplayIdentifier);

        var formComponent = _model.RootComponents.First(c => c.TypeCode == 60);
        Assert.Equal("Trip main form", formComponent.DisplayIdentifier);
    }
}
