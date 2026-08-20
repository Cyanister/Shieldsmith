namespace Shieldsmith.Diagrams;

/// <summary>
/// Colours and type for rendered diagrams. Defaults match the Shieldsmith brand
/// (cameronshields.co.uk): teal primary on a light slate canvas, Inter type.
/// </summary>
public sealed class DiagramTheme
{
    public string Primary { get; set; } = "#00AAAA";
    public string PrimaryDark { get; set; } = "#008B8B";
    public string TintLight { get; set; } = "#F0FAFA";
    public string TintMid { get; set; } = "#E8F7F7";
    public string TintStrong { get; set; } = "#C8EEEE";
    public string Text { get; set; } = "#0F172A";
    public string Muted { get; set; } = "#475569";
    public string Border { get; set; } = "#E2E8F0";
    public string Surface { get; set; } = "#FFFFFF";
    public string Canvas { get; set; } = "#FFFFFF";
    public string EdgeColour { get; set; } = "#64748B";

    public string FontFamily { get; set; } = "Inter, Segoe UI, system-ui, sans-serif";
    /// <summary>Concrete family used for measurement and PNG drawing.</summary>
    public string RenderFontFamily { get; set; } = "Segoe UI";
    public double CornerRadius { get; set; } = 8;

    public static DiagramTheme Brand => new();
}
