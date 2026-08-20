using System.Text.Json;
using Shieldsmith.Core.Export;
using Shieldsmith.Core.Models;

namespace Shieldsmith.Ai.Prompting;

/// <summary>
/// Builds every prompt Shieldsmith sends to a model. What is sent is schema-shaped
/// extraction only: table and relationship structure, flow structure, counts.
/// Environment variable values are redacted unless the user opts in, and every
/// prompt instructs the model to ground its statements in the supplied JSON and
/// say "not determinable from the solution" rather than guess.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Bumping this invalidates cached AI results after prompt changes.</summary>
    public const string PromptVersion = "1";

    public const string SystemPrompt =
        "You are helping document a Microsoft Power Platform solution. You are given structured JSON " +
        "extracted directly from the solution's export file. Ground every statement in that JSON. " +
        "If something cannot be determined from the supplied data, say 'not determinable from the solution' " +
        "rather than guessing. Do not invent components, columns, or behaviour. Write in plain, direct " +
        "British English with no marketing language. Never use em dashes; use a comma, colon or full stop.";

    private static readonly JsonSerializerOptions Compact = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string BuildPayload(SolutionModel model, bool redactEnvironmentValues = true) =>
        SolutionJsonExporter.ToJson(model, redactEnvironmentValues);

    public static AiRequest SolutionOverview(string payload) => new()
    {
        SystemPrompt = SystemPrompt,
        UserPrompt =
            "Here is the extracted structure of a Power Platform solution:\n\n" + payload + "\n\n" +
            "Write a plain-English overview of what this solution appears to be for: its purpose, its main " +
            "functional areas, and how the pieces fit together. Three to five paragraphs, no headings, no lists " +
            "of every component. State clearly where you are inferring purpose from names rather than reading it " +
            "from data.",
        MaxTokens = 1200,
    };

    public static AiRequest EntityDescription(SolutionModel model, EntityModel entity)
    {
        var slice = new
        {
            entity.SchemaName,
            entity.LogicalName,
            entity.DisplayName,
            entity.Description,
            attributes = entity.Attributes.Select(a => new
            {
                a.LogicalName,
                a.DisplayName,
                type = a.TypeDisplay,
                required = a.IsRequired,
                a.IsPrimaryId,
                a.IsPrimaryName,
            }),
            relationships = model.OneToManyRelationships
                .Where(r => Matches(r.ReferencedEntity, entity) || Matches(r.ReferencingEntity, entity))
                .Select(r => new { r.SchemaName, one = r.ReferencedEntity, many = r.ReferencingEntity })
                .Concat<object>(model.ManyToManyRelationships
                    .Where(r => Matches(r.Entity1, entity) || Matches(r.Entity2, entity))
                    .Select(r => new { r.SchemaName, r.Entity1, r.Entity2, r.IntersectEntity })),
        };

        return new AiRequest
        {
            SystemPrompt = SystemPrompt,
            UserPrompt =
                "Here is one table from a Power Platform solution:\n\n" +
                JsonSerializer.Serialize(slice, Compact) + "\n\n" +
                "In one short paragraph, describe the role this table appears to play in the solution and its " +
                "key relationships. Plain English, no repetition of the raw column list.",
            MaxTokens = 400,
        };
    }

    public static AiRequest FlowDescription(ProcessModel process)
    {
        var flow = process.CloudFlow;
        var slice = new
        {
            process.Name,
            kind = process.KindDisplay,
            trigger = flow?.Trigger?.Summary,
            actions = flow is null ? null : SerializeActions(flow.Actions),
            connectors = flow?.ConnectorsUsed,
        };

        return new AiRequest
        {
            SystemPrompt = SystemPrompt,
            UserPrompt =
                "Here is one automation from a Power Platform solution:\n\n" +
                JsonSerializer.Serialize(slice, Compact) + "\n\n" +
                "In one short paragraph, describe what this automation does: when it runs, what it touches, " +
                "and what the outcome appears to be. Plain English.",
            MaxTokens = 400,
        };
    }

    public static AiRequest RiskFlags(string payload) => new()
    {
        SystemPrompt = SystemPrompt,
        UserPrompt =
            "Here is the extracted structure of a Power Platform solution:\n\n" + payload + "\n\n" +
            "List any risk or quality observations supported by this data, for example: automations whose " +
            "purpose overlaps, tables with very large column counts, heavy use of many-to-many relationships, " +
            "environment variables that look unused, or draft automations. For each observation say what in the " +
            "data supports it. If the data supports no observations, say so. Use a short bullet list.",
        MaxTokens = 1000,
    };

    private static object SerializeActions(IEnumerable<FlowAction> actions) =>
        actions.Select(a => new
        {
            name = a.DisplayName,
            operation = a.OperationDisplay,
            children = SerializeActions(a.Children),
        });

    private static bool Matches(string name, EntityModel entity) =>
        string.Equals(name, entity.SchemaName, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, entity.LogicalName, StringComparison.OrdinalIgnoreCase);
}
