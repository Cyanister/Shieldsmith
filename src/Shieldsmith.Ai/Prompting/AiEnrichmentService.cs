using Shieldsmith.Core.Models;

namespace Shieldsmith.Ai.Prompting;

/// <summary>
/// Runs the enrichment pass: solution overview, per-table and per-flow
/// descriptions, and risk observations. Every result is cached, every failure
/// degrades to "AI content unavailable" for that block, and the document
/// remains complete without any of it.
/// </summary>
public sealed class AiEnrichmentService
{
    private readonly IAiProvider _provider;
    private readonly AiCache _cache;

    public AiEnrichmentService(IAiProvider provider, AiCache? cache = null)
    {
        _provider = provider;
        _cache = cache ?? new AiCache();
    }

    public sealed class Options
    {
        public bool RedactEnvironmentValues { get; set; } = true;
        public bool IncludePerEntity { get; set; } = true;
        public bool IncludePerFlow { get; set; } = true;
        public bool IncludeRiskFlags { get; set; } = true;
        /// <summary>Cap on per-component calls, largest solutions first come first.</summary>
        public int MaxComponentCalls { get; set; } = 40;
        public bool ForceRefresh { get; set; }
    }

    public async Task<GeneratedInterpretation> EnrichAsync(
        SolutionModel model,
        Options? options = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new Options();
        var enrichment = new GeneratedInterpretation
        {
            Provider = _provider.Name,
            Model = _provider.Model,
            GeneratedAtUtc = DateTime.UtcNow,
        };

        var payload = PromptBuilder.BuildPayload(model, options.RedactEnvironmentValues);

        progress?.Report("AI: solution overview...");
        enrichment.SolutionOverview = await RunAsync(
            model.UniqueName, PromptBuilder.SolutionOverview(payload), options, enrichment, cancellationToken);

        var callsRemaining = options.MaxComponentCalls;

        if (options.IncludePerEntity)
        {
            foreach (var entity in model.Entities
                         .Where(e => e.Attributes.Count > 0)
                         .OrderByDescending(e => e.Attributes.Count))
            {
                if (callsRemaining-- <= 0)
                {
                    enrichment.Notes.Add("Per-table AI descriptions were capped; remaining tables have none.");
                    break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"AI: table {entity.DisplayName}...");
                var text = await RunAsync(model.UniqueName,
                    PromptBuilder.EntityDescription(model, entity), options, enrichment, cancellationToken);
                if (text is not null)
                    enrichment.EntityDescriptions[entity.LogicalName] = text;
            }
        }

        if (options.IncludePerFlow)
        {
            foreach (var process in model.Processes.Where(p => p.CloudFlow is not null))
            {
                if (callsRemaining-- <= 0)
                {
                    enrichment.Notes.Add("Per-flow AI descriptions were capped; remaining flows have none.");
                    break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report($"AI: flow {process.Name}...");
                var text = await RunAsync(model.UniqueName,
                    PromptBuilder.FlowDescription(process), options, enrichment, cancellationToken);
                if (text is not null)
                    enrichment.FlowDescriptions[process.Name] = text;
            }
        }

        if (options.IncludeRiskFlags)
        {
            progress?.Report("AI: risk observations...");
            enrichment.RiskObservations = await RunAsync(
                model.UniqueName, PromptBuilder.RiskFlags(payload), options, enrichment, cancellationToken);
        }

        return enrichment;
    }

    private async Task<string?> RunAsync(
        string solutionName, AiRequest request, Options options,
        GeneratedInterpretation enrichment, CancellationToken cancellationToken)
    {
        var key = _cache.KeyFor(request, _provider.Name, _provider.Model);
        if (!options.ForceRefresh && _cache.Get(solutionName, key) is { } cached)
        {
            return cached.Text;
        }

        AiResult result;
        try
        {
            result = await _provider.CompleteAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result = AiResult.Fail(ex.Message);
        }

        if (!result.Success)
        {
            enrichment.Notes.Add($"AI content unavailable for one section: {result.Error}");
            return null;
        }

        enrichment.TotalInputTokens += result.InputTokens;
        enrichment.TotalOutputTokens += result.OutputTokens;
        _cache.Put(solutionName, key, new AiCache.Entry
        {
            Text = result.Text,
            Provider = _provider.Name,
            Model = _provider.Model,
            GeneratedAtUtc = DateTime.UtcNow,
        });
        return result.Text;
    }
}
