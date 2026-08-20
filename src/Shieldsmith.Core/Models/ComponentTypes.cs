namespace Shieldsmith.Core.Models;

/// <summary>
/// The documented solution component type codes. Anything not listed renders as
/// "Component type N" rather than a guess.
/// </summary>
public static class ComponentTypes
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [1] = "Table (entity)",
        [2] = "Column (attribute)",
        [3] = "Relationship",
        [9] = "Choice (option set)",
        [10] = "Table relationship",
        [11] = "Table relationship role",
        [13] = "Managed property",
        [14] = "Alternate key",
        [16] = "Privilege",
        [20] = "Security role",
        [22] = "Display string",
        [24] = "Form",
        [25] = "Organisation",
        [26] = "View (saved query)",
        [29] = "Process (workflow or cloud flow)",
        [31] = "Report",
        [35] = "Attachment",
        [36] = "Email template",
        [37] = "Contract template",
        [38] = "KB article template",
        [39] = "Mail merge template",
        [44] = "Duplicate rule",
        [46] = "Table map",
        [47] = "Column map",
        [48] = "Ribbon command",
        [50] = "Ribbon customisation",
        [59] = "Chart (saved query visualisation)",
        [60] = "System form",
        [61] = "Web resource",
        [62] = "Site map",
        [63] = "Connection role",
        [65] = "Hierarchy rule",
        [66] = "Custom control (PCF)",
        [68] = "Custom control default config",
        [70] = "Field security profile",
        [71] = "Field permission",
        [80] = "Model-driven app (app module)",
        [90] = "Plug-in type",
        [91] = "Plug-in assembly",
        [92] = "SDK message processing step",
        [93] = "SDK message processing step image",
        [95] = "Service endpoint",
        [150] = "Routing rule",
        [152] = "SLA",
        [154] = "Convert rule",
        [161] = "Mobile offline profile",
        [165] = "Similarity rule",
        [300] = "Canvas app",
        [371] = "Connector",
        [372] = "Connector (custom)",
        [380] = "Environment variable definition",
        [381] = "Environment variable value",
        [400] = "AI project type",
        [401] = "AI project",
        [402] = "AI configuration",
        [430] = "Entity analytics configuration",
        [431] = "Column image configuration",
        [432] = "Table image configuration",
    };

    public static string NameOf(int typeCode) =>
        Names.TryGetValue(typeCode, out var name) ? name : $"Component type {typeCode}";
}
