using Shieldsmith.Ai;
using Shieldsmith.Ai.Prompting;
using Shieldsmith.Ai.Providers;
using Shieldsmith.Core.Models;
using Shieldsmith.Core.Parsing;
using Xunit;

namespace Shieldsmith.Ai.Tests;

/// <summary>A provider that records calls and returns canned text; no network.</summary>
internal sealed class FakeProvider : IAiProvider
{
    public int Calls { get; private set; }
    public bool FailAll { get; set; }
    public string Name => "Fake";
    public string Model => "fake-1";

    public Task<AiAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AiAvailability.Yes("fake"));

    public Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult(FailAll
            ? AiResult.Fail("simulated failure")
            : AiResult.Ok($"canned answer {Calls}", 100, 50));
    }
}

public sealed class AiTests : IDisposable
{
    private readonly UnpackedSolution _unpacked;
    private readonly SolutionModel _model;
    private readonly string _tempDir;

    public AiTests()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "data", "ContosoTravel_sample.zip")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        _unpacked = SolutionUnpacker.Unpack(Path.Combine(dir!.FullName, "data", "ContosoTravel_sample.zip"));
        _model = SolutionParser.Parse(_unpacked);
        _tempDir = Path.Combine(Path.GetTempPath(), $"shieldsmith-ai-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _unpacked.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void Payload_redacts_environment_values_by_default()
    {
        var redacted = PromptBuilder.BuildPayload(_model);
        Assert.DoesNotContain("https://api.contoso.example/travel", redacted);
        Assert.Contains("[redacted]", redacted);
        // Schema names still present: names are structure, values are data.
        Assert.Contains("ct_ApiBaseUrl", redacted);

        var unredacted = PromptBuilder.BuildPayload(_model, redactEnvironmentValues: false);
        Assert.Contains("https://api.contoso.example/travel", unredacted);
    }

    [Fact]
    public async Task Enrichment_produces_overview_entity_and_flow_descriptions()
    {
        var provider = new FakeProvider();
        var service = new AiEnrichmentService(provider, new AiCache(_tempDir));
        var interpretation = await service.EnrichAsync(_model);

        Assert.Equal("Fake", interpretation.Provider);
        Assert.NotNull(interpretation.SolutionOverview);
        Assert.NotNull(interpretation.RiskObservations);
        Assert.Equal(4, interpretation.EntityDescriptions.Count);
        Assert.Equal(2, interpretation.FlowDescriptions.Count);
        Assert.True(interpretation.TotalInputTokens > 0);
        Assert.Contains("Generated interpretation (Fake, fake-1", interpretation.Attribution);
    }

    [Fact]
    public async Task Enrichment_results_are_cached_and_reused()
    {
        var cache = new AiCache(_tempDir);
        var provider = new FakeProvider();
        var service = new AiEnrichmentService(provider, cache);

        await service.EnrichAsync(_model);
        var callsAfterFirstRun = provider.Calls;
        Assert.True(callsAfterFirstRun > 0);

        await service.EnrichAsync(_model);
        Assert.Equal(callsAfterFirstRun, provider.Calls); // every section came from cache

        await service.EnrichAsync(_model, new AiEnrichmentService.Options { ForceRefresh = true });
        Assert.Equal(callsAfterFirstRun * 2, provider.Calls);
    }

    [Fact]
    public async Task Failures_degrade_to_notes_not_exceptions()
    {
        var provider = new FakeProvider { FailAll = true };
        var service = new AiEnrichmentService(provider, new AiCache(_tempDir));
        var interpretation = await service.EnrichAsync(_model);

        Assert.Null(interpretation.SolutionOverview);
        Assert.Empty(interpretation.EntityDescriptions);
        Assert.NotEmpty(interpretation.Notes);
        Assert.All(interpretation.Notes.Where(n => n.Contains("unavailable")),
            note => Assert.Contains("simulated failure", note));
    }

    [Fact]
    public void Cache_key_changes_with_provider_model_and_prompt()
    {
        var cache = new AiCache(_tempDir);
        var request = new AiRequest { SystemPrompt = "s", UserPrompt = "u" };
        var baseline = cache.KeyFor(request, "p1", "m1");
        Assert.NotEqual(baseline, cache.KeyFor(request, "p2", "m1"));
        Assert.NotEqual(baseline, cache.KeyFor(request, "p1", "m2"));
        Assert.NotEqual(baseline, cache.KeyFor(new AiRequest { SystemPrompt = "s", UserPrompt = "other" }, "p1", "m1"));
        Assert.Equal(baseline, cache.KeyFor(new AiRequest { SystemPrompt = "s", UserPrompt = "u" }, "p1", "m1"));
    }

    [Fact]
    public void Key_store_round_trips_and_removes_keys()
    {
        var storePath = Path.Combine(_tempDir, "keys.json");
        var store = new SecureKeyStore(storePath);

        Assert.False(store.HasKey("Anthropic"));
        store.SetKey("Anthropic", "sk-ant-test-123");
        Assert.Equal("sk-ant-test-123", store.GetKey("Anthropic"));

        // The key is not stored in the clear.
        Assert.DoesNotContain("sk-ant-test-123", File.ReadAllText(storePath));

        store.SetKey("Anthropic", string.Empty);
        Assert.False(store.HasKey("Anthropic"));
    }

    [Fact]
    public async Task Claude_code_provider_without_cli_reports_unavailable_not_crash()
    {
        var provider = new ClaudeCodeCliProvider(cliPath: Path.Combine(_tempDir, "does-not-exist.exe"));
        // The constructor keeps the given path even if missing; a run then fails cleanly.
        var result = await provider.CompleteAsync(new AiRequest { UserPrompt = "hi" });
        Assert.False(result.Success);

        var absent = new ClaudeCodeCliProvider(cliPath: null);
        // With no path given it falls back to discovery; whichever way that goes,
        // availability is a state, never an exception.
        var availability = await absent.CheckAvailabilityAsync();
        Assert.NotNull(availability.Detail);
    }
}
