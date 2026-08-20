namespace Shieldsmith.Core.Models;

/// <summary>
/// AI-generated interpretation of a solution. This is commentary, not extracted
/// fact: writers must render it only inside clearly labelled blocks, never mix
/// it into fact tables, and every document must remain complete without it.
/// </summary>
public sealed class GeneratedInterpretation
{
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DateTime GeneratedAtUtc { get; set; }
    public string? SolutionOverview { get; set; }
    /// <summary>Keyed by entity logical name.</summary>
    public Dictionary<string, string> EntityDescriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Keyed by process name.</summary>
    public Dictionary<string, string> FlowDescriptions { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? RiskObservations { get; set; }
    public List<string> Notes { get; } = new();
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }

    public string Attribution =>
        $"Generated interpretation ({Provider}, {Model}, {GeneratedAtUtc:d MMMM yyyy}). " +
        "Verify against the extracted facts in this document.";
}
