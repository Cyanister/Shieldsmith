using System.Text.RegularExpressions;
using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads plugin assemblies, the plugin types inside them, and the SDK message
/// processing steps that register those types against a table and a message.
///
/// Written against a real export. Assemblies live at
/// <c>ImportExportXml/PluginAssemblies/PluginAssembly</c> with their types
/// nested, and the registrations at
/// <c>ImportExportXml/SdkMessageProcessingSteps/SdkMessageProcessingStep</c>,
/// linked back by <c>PluginTypeName</c>. PowerDocu documents neither, so a
/// plugin is invisible in every other tool that reads an export.
/// </summary>
public static class PluginParser
{
    public static void Parse(XDocument customizations, SolutionModel model)
    {
        var root = customizations.Root;
        if (root is null) return;

        foreach (var element in root.Descendants("PluginAssembly"))
        {
            var assembly = new PluginAssemblyModel
            {
                FullName = element.Attribute("FullName")?.Value ?? string.Empty,
                Id = (element.Attribute("PluginAssemblyId")?.Value ?? string.Empty).Trim('{', '}'),
                FileName = element.Element("FileName")?.Value ?? string.Empty,
                IsolationMode = IsolationModeName(element.Element("IsolationMode")?.Value),
                Version = VersionFrom(element.Attribute("FullName")?.Value),
            };
            assembly.Name = NameFrom(assembly.FullName);

            foreach (var type in element.Descendants("PluginType"))
            {
                assembly.Types.Add(new PluginTypeModel
                {
                    Name = type.Attribute("Name")?.Value ?? string.Empty,
                    FriendlyName = type.Element("FriendlyName")?.Value ?? string.Empty,
                    Id = (type.Attribute("PluginTypeId")?.Value ?? string.Empty).Trim('{', '}'),
                    AssemblyQualifiedName = type.Attribute("AssemblyQualifiedName")?.Value ?? string.Empty,
                });
            }

            model.PluginAssemblies.Add(assembly);
        }

        foreach (var element in root.Descendants("SdkMessageProcessingStep"))
        {
            var name = element.Attribute("Name")?.Value ?? string.Empty;
            var step = new SdkMessageStepModel
            {
                Name = name,
                Id = (element.Attribute("SdkMessageProcessingStepId")?.Value ?? string.Empty).Trim('{', '}'),
                PluginTypeName = element.Element("PluginTypeName")?.Value ?? string.Empty,
                PrimaryEntity = element.Element("PrimaryEntity")?.Value ?? string.Empty,
                Stage = StageName(element.Element("Stage")?.Value),
                Mode = string.Equals(element.Element("Mode")?.Value, "1", StringComparison.Ordinal)
                    ? "Asynchronous"
                    : "Synchronous",
                Rank = int.TryParse(element.Element("Rank")?.Value, out var rank) ? rank : 0,
            };
            step.FilteringAttributes.AddRange(SplitAttributes(element.Element("FilteringAttributes")?.Value));
            step.Message = MessageFrom(name, step.PrimaryEntity);
            step.ImageCount = element.Descendants("SdkMessageProcessingStepImage").Count();

            // Attach to its assembly where the type can be matched, so the
            // document can list steps under the code that implements them.
            var owner = model.PluginAssemblies.FirstOrDefault(a =>
                a.Types.Any(t => step.PluginTypeName.StartsWith(t.Name + ",", StringComparison.Ordinal) ||
                                 string.Equals(t.Name, step.PluginTypeName, StringComparison.Ordinal)));
            if (owner is not null) owner.Steps.Add(step);

            model.SdkMessageSteps.Add(step);
        }

        if (model.SdkMessageSteps.Count > 0 && model.PluginAssemblies.Count == 0)
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                $"{model.SdkMessageSteps.Count} plugin step(s) are registered but their assembly is not " +
                "in this solution, so the code behind them is documented elsewhere."));
    }

    /// <summary>
    /// The message is not stated as a name anywhere in the export: the step
    /// carries only an SdkMessageId GUID. The default step name is
    /// "&lt;Plugin&gt;: &lt;Message&gt; of &lt;table&gt;", so it is read from
    /// there and only trusted when the table it names matches PrimaryEntity.
    /// A renamed step yields nothing rather than a guess.
    /// </summary>
    internal static string MessageFrom(string stepName, string primaryEntity)
    {
        var match = Regex.Match(stepName, @":\s*(?<message>[A-Za-z]+)\s+of\s+(?<entity>\S+)\s*$");
        if (!match.Success) return string.Empty;
        if (primaryEntity.Length > 0 &&
            !string.Equals(match.Groups["entity"].Value, primaryEntity, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return match.Groups["message"].Value;
    }

    private static List<string> SplitAttributes(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>The pipeline stage. The raw number is kept when it is not one of the three.</summary>
    private static string StageName(string? value) => value switch
    {
        "10" => "Pre-validation",
        "20" => "Pre-operation",
        "40" => "Post-operation",
        null or "" => string.Empty,
        _ => $"Stage {value}",
    };

    private static string IsolationModeName(string? value) => value switch
    {
        "1" => "None (full trust)",
        "2" => "Sandbox",
        null or "" => string.Empty,
        _ => $"Isolation mode {value}",
    };

    /// <summary>"AlumascRoofing.Plugins, Version=1.0.0.0, ..." gives "AlumascRoofing.Plugins".</summary>
    private static string NameFrom(string fullName)
    {
        var comma = fullName.IndexOf(',');
        return comma < 0 ? fullName.Trim() : fullName[..comma].Trim();
    }

    private static string VersionFrom(string? fullName)
    {
        if (fullName is null) return string.Empty;
        var match = Regex.Match(fullName, @"Version=(?<version>[\d.]+)");
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }
}
