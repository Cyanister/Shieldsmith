using System.Xml.Linq;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Core.Parsing;

/// <summary>
/// Reads the stages and steps of a business process flow from its workflow XAML.
///
/// Written against real exports. A BPF is a workflow with Category 4 whose XAML
/// is Windows Workflow Foundation markup, not a friendly definition format. The
/// stages are <c>ActivityReference</c> elements whose AssemblyQualifiedName is
/// <c>Microsoft.Crm.Workflow.Activities.StageComposite</c>, and their
/// <c>DisplayName</c> carries a generated prefix: "StageStep3: Under
/// Preparation". The steps inside a stage are <c>Control</c> elements from the
/// <c>mcwb</c> namespace, each naming the column it is bound to.
///
/// Neither PowerDocu nor the maker portal exports this in a readable form, so
/// this is the only place the stage list exists outside the designer.
/// </summary>
public static class BusinessProcessFlowParser
{
    private const string StageActivity = "Microsoft.Crm.Workflow.Activities.StageComposite";

    public static void Attach(ProcessModel process, string rootPath, SolutionModel model)
    {
        if (string.IsNullOrEmpty(process.FileName)) return;

        // XamlFileName is recorded with a leading slash and forward slashes.
        var relative = process.FileName.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(rootPath, relative);
        if (!File.Exists(path))
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                $"Business process flow '{process.Name}' references {process.FileName}, " +
                "which is not in the export, so its stages could not be read."));
            return;
        }

        XDocument document;
        try
        {
            document = XDocument.Load(path);
        }
        catch (Exception ex)
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Warning,
                $"Business process flow '{process.Name}' has XAML that could not be read ({ex.Message})."));
            return;
        }

        var detail = new BusinessProcessFlowDetail();
        var order = 0;

        foreach (var stageElement in document.Descendants()
                     .Where(e => e.Name.LocalName == "ActivityReference" &&
                                 (e.Attribute("AssemblyQualifiedName")?.Value ?? string.Empty)
                                 .StartsWith(StageActivity, StringComparison.Ordinal)))
        {
            var stage = new BusinessProcessStage
            {
                Name = StripStagePrefix(stageElement.Attribute("DisplayName")?.Value ?? string.Empty),
                Order = ++order,
            };

            foreach (var control in stageElement.Descendants()
                         .Where(e => e.Name.LocalName == "Control"))
            {
                var isSystem = string.Equals(control.Attribute("IsSystemControl")?.Value,
                    "True", StringComparison.OrdinalIgnoreCase);
                stage.Steps.Add(new BusinessProcessStep
                {
                    Name = control.Attribute("ControlDisplayName")?.Value ?? string.Empty,
                    ControlId = control.Attribute("ControlId")?.Value ?? string.Empty,
                    DataField = control.Attribute("DataFieldName")?.Value ?? string.Empty,
                    IsSystemControl = isSystem,
                });
            }

            detail.Stages.Add(stage);
        }

        if (detail.Stages.Count == 0)
        {
            model.Diagnostics.Add(new Diagnostic(DiagnosticSeverity.Info,
                $"Business process flow '{process.Name}' declares no stages in its XAML."));
            return;
        }

        process.BusinessProcessFlow = detail;
    }

    /// <summary>
    /// "StageStep3: Under Preparation" becomes "Under Preparation". The prefix
    /// is a generated activity name, not something a maker ever typed, so it is
    /// noise in a document. A stage genuinely named with a colon keeps the rest
    /// of its name intact because only the first separator is removed.
    /// </summary>
    internal static string StripStagePrefix(string displayName)
    {
        var separator = displayName.IndexOf(':');
        if (separator < 0) return displayName.Trim();

        var prefix = displayName.AsSpan(0, separator);
        if (!prefix.StartsWith("StageStep", StringComparison.Ordinal)) return displayName.Trim();

        return displayName[(separator + 1)..].Trim();
    }
}
