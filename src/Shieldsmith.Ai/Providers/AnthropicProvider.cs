using Anthropic;
using Anthropic.Models.Messages;

namespace Shieldsmith.Ai.Providers;

/// <summary>
/// Claude via the user's own Anthropic API key, using the official Anthropic SDK.
/// </summary>
public sealed class AnthropicProvider : IAiProvider
{
    public const string ProviderName = "Anthropic";
    public const string DefaultModel = "claude-opus-5";

    private readonly AnthropicClient _client;

    public AnthropicProvider(string apiKey, string? model = null)
    {
        _client = new AnthropicClient { ApiKey = apiKey };
        Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
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
                ? AiAvailability.Yes($"Anthropic API reachable, model {Model}.")
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
            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = Model,
                MaxTokens = request.MaxTokens,
                System = new List<TextBlockParam> { new() { Text = request.SystemPrompt } },
                Messages = [new() { Role = Role.User, Content = request.UserPrompt }],
            });

            if (response.StopReason == "refusal")
                return AiResult.Fail("The model declined this request.");

            var text = string.Join("\n",
                response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
            if (string.IsNullOrWhiteSpace(text))
                return AiResult.Fail("The model returned no text.");

            return AiResult.Ok(text.Trim(),
                response.Usage?.InputTokens ?? 0,
                response.Usage?.OutputTokens ?? 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return AiResult.Fail($"Anthropic request failed: {ex.Message}");
        }
    }
}
