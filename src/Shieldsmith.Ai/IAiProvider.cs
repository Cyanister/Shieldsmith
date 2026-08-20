namespace Shieldsmith.Ai;

/// <summary>
/// One AI backend: the user's own Anthropic key, their own OpenAI key, or an
/// installed Claude Code CLI. Providers return structured results, never throw
/// for content-level failures, and a refusal is a state, not an exception.
/// </summary>
public interface IAiProvider
{
    string Name { get; }
    string Model { get; }
    Task<AiAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default);
    Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default);
}

public sealed class AiRequest
{
    public string SystemPrompt { get; set; } = string.Empty;
    public string UserPrompt { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 1500;
}

public sealed class AiResult
{
    public bool Success { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Error { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    public static AiResult Ok(string text, long inputTokens = 0, long outputTokens = 0) =>
        new() { Success = true, Text = text, InputTokens = inputTokens, OutputTokens = outputTokens };

    public static AiResult Fail(string error) =>
        new() { Success = false, Error = error };
}

public sealed class AiAvailability
{
    public bool IsAvailable { get; set; }
    public string Detail { get; set; } = string.Empty;

    public static AiAvailability Yes(string detail) => new() { IsAvailable = true, Detail = detail };
    public static AiAvailability No(string detail) => new() { IsAvailable = false, Detail = detail };
}
