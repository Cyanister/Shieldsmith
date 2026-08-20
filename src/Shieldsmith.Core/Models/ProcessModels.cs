namespace Shieldsmith.Core.Models;

/// <summary>
/// A process from the Workflows block: a cloud flow, classic workflow, business
/// rule, action, or business process flow.
/// </summary>
public sealed class ProcessModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Category { get; set; }
    public string PrimaryEntity { get; set; } = string.Empty;
    /// <summary>StateCode 1 in the export means activated.</summary>
    public bool IsActive { get; set; }
    public string FileName { get; set; } = string.Empty;
    public ProcessKind Kind => Category switch
    {
        0 => ProcessKind.ClassicWorkflow,
        1 => ProcessKind.Dialog,
        2 => ProcessKind.BusinessRule,
        3 => ProcessKind.Action,
        4 => ProcessKind.BusinessProcessFlow,
        5 => ProcessKind.CloudFlow,
        6 => ProcessKind.DesktopFlow,
        _ => ProcessKind.Other,
    };
    public string KindDisplay => Kind switch
    {
        ProcessKind.ClassicWorkflow => "Classic workflow",
        ProcessKind.Dialog => "Dialog",
        ProcessKind.BusinessRule => "Business rule",
        ProcessKind.Action => "Action",
        ProcessKind.BusinessProcessFlow => "Business process flow",
        ProcessKind.CloudFlow => "Cloud flow",
        ProcessKind.DesktopFlow => "Desktop flow",
        _ => $"Process (category {Category})",
    };

    /// <summary>Present only for cloud flows whose definition file was readable.</summary>
    public CloudFlowDetail? CloudFlow { get; set; }

    /// <summary>Present only for desktop flows whose Robin script was readable.</summary>
    public DesktopFlowDetail? DesktopFlow { get; set; }

    /// <summary>Stages and steps, when this is a business process flow.</summary>
    public BusinessProcessFlowDetail? BusinessProcessFlow { get; set; }

    /// <summary>
    /// Power Automate Desktop flows carry a UIFlowType; 2 is Power Automate
    /// Desktop. Its presence alongside category 6 is the reliable signal.
    /// </summary>
    public int? UiFlowType { get; set; }
}

/// <summary>
/// A desktop flow's Robin script, broken into subflows and action steps.
/// </summary>
/// <summary>
/// A business process flow's stages in order, each with the steps a user fills
/// in. Read from the workflow XAML; see BusinessProcessFlowParser.
/// </summary>
public sealed class BusinessProcessFlowDetail
{
    public List<BusinessProcessStage> Stages { get; } = new();

    public int StepCount => Stages.Sum(s => s.Steps.Count);
}

public sealed class BusinessProcessStage
{
    public string Name { get; set; } = string.Empty;
    /// <summary>Position in the flow, from one.</summary>
    public int Order { get; set; }
    public List<BusinessProcessStep> Steps { get; } = new();
}

public sealed class BusinessProcessStep
{
    public string Name { get; set; } = string.Empty;
    public string ControlId { get; set; } = string.Empty;
    /// <summary>The column this step writes to; empty for an unbound step.</summary>
    public string DataField { get; set; } = string.Empty;
    /// <summary>System controls are added by the platform, not by the maker.</summary>
    public bool IsSystemControl { get; set; }
}

public sealed class DesktopFlowDetail
{
    public string SchemaVersion { get; set; } = string.Empty;
    public List<DesktopFlowVariable> Inputs { get; } = new();
    public List<DesktopFlowVariable> Outputs { get; } = new();
    /// <summary>The main flow's steps, plus one entry per FUNCTION subflow.</summary>
    public List<DesktopFlowSubflow> Subflows { get; } = new();
    /// <summary>Distinct Robin modules used, for example Excel or WebAutomation.</summary>
    public List<string> Modules { get; } = new();

    public int TotalSteps => Subflows.Sum(s => s.Steps.Count);
}

public sealed class DesktopFlowSubflow
{
    /// <summary>"Main" for the top-level script, otherwise the FUNCTION name.</summary>
    public string Name { get; set; } = "Main";
    public bool IsGlobal { get; set; }
    public List<DesktopFlowStep> Steps { get; } = new();
}

public sealed class DesktopFlowStep
{
    /// <summary>For example Excel.LaunchExcel.LaunchAndOpen.</summary>
    public string Action { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    /// <summary>Nesting depth inside IF, LOOP or ON ERROR blocks.</summary>
    public int Depth { get; set; }
    public bool IsDisabled { get; set; }
    /// <summary>Variables the step binds its outputs to.</summary>
    public List<string> OutputVariables { get; } = new();

    public string Display => string.IsNullOrEmpty(Module) ? Action : Action;
}

public sealed class DesktopFlowVariable
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public enum ProcessKind
{
    ClassicWorkflow,
    Dialog,
    BusinessRule,
    Action,
    BusinessProcessFlow,
    CloudFlow,
    DesktopFlow,
    Other,
}

public sealed class CloudFlowDetail
{
    public FlowTrigger? Trigger { get; set; }
    public List<FlowAction> Actions { get; } = new();
    public List<FlowConnectionReference> ConnectionReferences { get; } = new();

    public IEnumerable<string> ConnectorsUsed =>
        ConnectionReferences.Select(r => r.ApiName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    public IEnumerable<FlowAction> AllActions()
    {
        static IEnumerable<FlowAction> Walk(FlowAction action)
        {
            yield return action;
            foreach (var child in action.Children)
                foreach (var nested in Walk(child))
                    yield return nested;
        }
        return Actions.SelectMany(Walk);
    }
}

public sealed class FlowTrigger
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string Connector { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Recurrence { get; set; } = string.Empty;

    /// <summary>One-line plain description assembled from extracted parts only.</summary>
    public string Summary
    {
        get
        {
            if (!string.IsNullOrEmpty(EntityName))
                return $"When a {EntityName} row is {(string.IsNullOrEmpty(Message) ? "changed" : Message)}";
            if (!string.IsNullOrEmpty(Recurrence))
                return $"On a schedule: {Recurrence}";
            if (Type == "Request" && Kind == "PowerAppV2")
                return "Manually triggered from a Power App or another flow";
            if (Type == "Request" && Kind == "Button")
                return "Manually triggered (instant flow)";
            if (!string.IsNullOrEmpty(OperationId))
                return $"{OperationId}{(string.IsNullOrEmpty(Connector) ? string.Empty : $" ({Connector})")}";
            return string.IsNullOrEmpty(Type) ? Name : Type;
        }
    }
}

public sealed class FlowAction
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string Connector { get; set; } = string.Empty;
    /// <summary>Names of actions this one runs after, from runAfter.</summary>
    public List<string> RunAfter { get; } = new();
    public List<FlowAction> Children { get; } = new();

    public string DisplayName => Name.Replace('_', ' ');
    public string OperationDisplay =>
        string.IsNullOrEmpty(OperationId)
            ? Type
            : $"{OperationId}{(string.IsNullOrEmpty(Connector) ? string.Empty : $" ({Connector})")}";
}

public sealed class FlowConnectionReference
{
    public string ApiName { get; set; } = string.Empty;
    public string LogicalName { get; set; } = string.Empty;
}

/// <summary>A connection reference declared at solution level in customizations.xml.</summary>
public sealed class ConnectionReferenceModel
{
    public string LogicalName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ConnectorId { get; set; } = string.Empty;

    public string ConnectorShortName
    {
        get
        {
            var index = ConnectorId.LastIndexOf('/');
            return index >= 0 ? ConnectorId[(index + 1)..] : ConnectorId;
        }
    }
}
