using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Agents;

/// <summary>
/// Dev/test double implementing <see cref="IChatClient"/> directly: echoes the last user
/// message back, character by character when streamed, with a small delay to simulate token
/// latency. Replaces <c>agent.py</c>'s <c>EchoProvider</c> (a pydantic-ai <c>Agent</c>
/// subclass); selected the same way — via a model config with provider "subconscious" and
/// model "echo" (see <see cref="AgentManager.BuildChatClient"/>).
/// </summary>
public sealed class EchoChatClient : IChatClient
{
    private static readonly TimeSpan CharacterDelay = TimeSpan.FromMilliseconds(20);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var text = LastUserText(messages);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, text));
        return Task.FromResult(response);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var text = LastUserText(messages);
        foreach (var ch in text)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new ChatResponseUpdate(ChatRole.Assistant, ch.ToString());
            await Task.Delay(CharacterDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    private static string LastUserText(IEnumerable<ChatMessage> messages)
    {
        var lastUser = messages.LastOrDefault(m => m.Role == ChatRole.User);
        return lastUser?.Text ?? string.Empty;
    }
}
