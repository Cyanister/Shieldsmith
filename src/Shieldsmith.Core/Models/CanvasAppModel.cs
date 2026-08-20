namespace Shieldsmith.Core.Models;

/// <summary>
/// A canvas app, read from its .msapp inside the solution export plus the
/// CanvasApp metadata block in customizations.xml.
/// </summary>
public sealed class CanvasAppModel
{
    /// <summary>Schema name, the join key with customizations.xml.</summary>
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>ISO timestamp from the solution metadata; the app's version.</summary>
    public string AppVersion { get; set; } = string.Empty;
    public string CreatedByClientVersion { get; set; } = string.Empty;
    /// <summary>0 is an app, 1 is a component or command library.</summary>
    public int CanvasAppType { get; set; }
    public string BackgroundColor { get; set; } = string.Empty;
    /// <summary>DesktopOrTablet or Phone, from the msapp's Properties.json.</summary>
    public string FormFactor { get; set; } = string.Empty;
    /// <summary>The msapp format version, not the app version.</summary>
    public string DocumentVersion { get; set; } = string.Empty;
    public DateTime? LastSavedUtc { get; set; }
    /// <summary>True when the .msapp was found and read.</summary>
    public bool HasInternals { get; set; }

    public bool IsComponentLibrary => CanvasAppType == 1;
    public string KindDisplay => IsComponentLibrary ? "Component library" : "Canvas app";

    public List<CanvasScreenModel> Screens { get; } = new();
    public List<CanvasDataSourceModel> DataSources { get; } = new();
    /// <summary>Global variables set with Set(), derived from formulas.</summary>
    public List<string> GlobalVariables { get; } = new();
    /// <summary>Collections created with Collect() or ClearCollect().</summary>
    public List<string> Collections { get; } = new();
    /// <summary>Control template name to count, from Properties.json ControlCount.</summary>
    public Dictionary<string, int> ControlCounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int TotalControls => Screens.Sum(s => s.CountControls());
}

public sealed class CanvasScreenModel
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Screen order within the app.</summary>
    public int Index { get; set; }
    public List<CanvasControlModel> Controls { get; } = new();

    public int CountControls() => Controls.Sum(c => c.CountSelfAndChildren());

    public IEnumerable<CanvasControlModel> AllControls() =>
        Controls.SelectMany(c => c.SelfAndDescendants());
}

public sealed class CanvasControlModel
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Template name, or the variant for a group container.</summary>
    public string Type { get; set; } = string.Empty;
    public List<CanvasControlModel> Children { get; } = new();
    /// <summary>Formulas the maker actually authored, property to Power Fx.</summary>
    public Dictionary<string, string> Properties { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int CountSelfAndChildren() => 1 + Children.Sum(c => c.CountSelfAndChildren());

    public IEnumerable<CanvasControlModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var descendant in child.SelfAndDescendants())
                yield return descendant;
    }
}

public sealed class CanvasDataSourceModel
{
    public string Name { get; set; } = string.Empty;
    /// <summary>The DataSources.json Type discriminator.</summary>
    public string Type { get; set; } = string.Empty;
    public string LogicalName { get; set; } = string.Empty;
    public string EntitySetName { get; set; } = string.Empty;

    public bool IsTable =>
        Type.Equals("NativeCDSDataSourceInfo", StringComparison.OrdinalIgnoreCase);
}
