namespace Shieldsmith.Core.Models;

public sealed class WebResourceModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int TypeCode { get; set; }
    public string FileName { get; set; } = string.Empty;

    public string TypeName => TypeCode switch
    {
        1 => "HTML",
        2 => "CSS",
        3 => "JavaScript",
        4 => "XML",
        5 => "PNG",
        6 => "JPG",
        7 => "GIF",
        8 => "Silverlight (XAP)",
        9 => "XSL",
        10 => "ICO",
        11 => "SVG",
        12 => "RESX",
        _ => $"Type {TypeCode}",
    };
}
