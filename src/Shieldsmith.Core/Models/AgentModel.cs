namespace Shieldsmith.Core.Models;

/// <summary>
/// A Copilot Studio agent. Agents are not declared in solution.xml at all, so
/// they are found by the presence of a bots/&lt;schemaname&gt;/bot.xml folder.
/// </summary>
public sealed class AgentModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string AuthenticationMode { get; set; } = string.Empty;
    /// <summary>Instructions from the .agent. component or the .gpt.default component.</summary>
    public string Instructions { get; set; } = string.Empty;
    public bool GenerativeActionsEnabled { get; set; }

    public List<AgentTopicModel> Topics { get; } = new();
    public List<AgentToolModel> Tools { get; } = new();
    public List<AgentKnowledgeModel> KnowledgeSources { get; } = new();

    public string AuthenticationModeDisplay => AuthenticationMode switch
    {
        "0" => "No authentication",
        "1" => "Only for Teams and Power Apps",
        "2" => "Authenticate manually",
        _ => string.IsNullOrEmpty(AuthenticationMode) ? "Not stated" : AuthenticationMode,
    };
}

public sealed class AgentTopicModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Phrases that trigger this topic.</summary>
    public List<string> TriggerQueries { get; } = new();
    /// <summary>Messages the topic sends back, in order.</summary>
    public List<string> Messages { get; } = new();
    /// <summary>Node kinds in the dialog, e.g. SendActivity, Question, BeginDialog.</summary>
    public List<string> ActionKinds { get; } = new();
}

public sealed class AgentToolModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>AgentDialog, TaskDialog, and so on.</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Schema name of the parent agent component, when the export states one.</summary>
    public string ParentComponent { get; set; } = string.Empty;
}

public sealed class AgentKnowledgeModel
{
    public string SchemaName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>For example PublicSiteSearchSource.</summary>
    public string SourceKind { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
}
