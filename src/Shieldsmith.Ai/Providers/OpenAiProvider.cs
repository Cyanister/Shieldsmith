using OpenAI.Chat;

namespace Shieldsmith.Ai.Providers;

/// <summary>
/// ChatGPT models via the user's own OpenAI API key, using the official OpenAI SDK.
/// The model is a user-editable string so new models need no Shieldsmith release.
/// </summary>
public sealed class OpenAiProvider : IAiProvider
{
    public const string ProviderName = "OpenAI";
    public const string DefaultModel = "gpt-4o";

    private readonly ChatClient _client;

    public OpenAiProvider(string apiKey, string? model = null)
    {
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _client = new ChatClient(Model, apiKey);
    }

    public string Name => ProviderName;
    public string Model { get; }

    public async Task<AiAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await CompleteAsync(new AiRequest
            {
                SystemPrompt = "Reply with the single word OK.",
                UserPrompt = "Say OK.",
                MaxTokens = 16,
            }, cancellationToken);
            return result.Success
                ? AiAvailability.Yes($"OpenAI API reachable, model {Model}.")
                : AiAvailability.No(result.Error ?? "Unknown error.");
        }
        catch (Exception ex)
        {
            return AiAvailability.No(ex.Message);
        }
    }

    public async Task<AiResult> CompleteAsync(AiRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatCompletion completion = await _client.CompleteChatAsync(
                new ChatMessage[]
                {
                    new SystemChatMessage(request.SystemPrompt),
                    new UserChatMessage(request.UserPrompt),
                },
                new ChatCompletionOptions { MaxOutputTokenCount = request.MaxTokens },
                cancellationToken);

            var text = string.Join("\n", completion.Content.Select(part => part.Text));
            if (string.IsNullOrWhiteSpace(text))
                return AiResult.Fail("The model returned no text.");

            return AiResult.Ok(text.Trim(),
                completion.Usage?.InputTokenCount ?? 0,
                completion.Usage?.OutputTokenCount ?? 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AiResult.Fail($"OpenAI request failed: {ex.Message}");
        }
    }
}
