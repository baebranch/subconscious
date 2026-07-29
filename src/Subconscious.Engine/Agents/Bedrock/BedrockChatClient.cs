using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Agents.Bedrock;

/// <summary>
/// Amazon Bedrock provider for Subconscious, exposed as a <see cref="IChatClient"/> so it plugs
/// into exactly the same seam every other provider uses (<see cref="AgentManager.BuildChatClient"/>)
/// and is therefore equally consumable by the engine's own chat loop and by an AG-UI endpoint.
///
/// <para>
/// This is the .NET counterpart of <c>agent.py</c>'s <c>_build_bedrock_model</c>. It talks to the
/// Bedrock Runtime <c>Converse</c>/<c>ConverseStream</c> API directly over HTTP:
/// <list type="bullet">
/// <item>request/response bodies are mapped by <see cref="BedrockConverseMapper"/>;</item>
/// <item>streaming responses are binary <c>application/vnd.amazon.eventstream</c> frames, decoded
/// by <see cref="AwsEventStreamDecoder"/> — Bedrock does not use SSE;</item>
/// <item>authentication uses a <b>Bedrock API key</b> (bearer token), matching the Python
/// implementation, whose stored <c>api_key</c> is documented as "a Bedrock bearer token, not an
/// AWS_ACCESS_KEY_ID".</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Scope limit — SigV4 is not implemented.</b> Requests are authenticated with a bearer token
/// only. Bedrock also accepts AWS SigV4-signed requests using access-key/secret/session
/// credentials, which <c>agent.py</c> could forward to boto3; those fields exist on
/// <see cref="ModelConfig"/> but are rejected here rather than silently ignored, so a
/// misconfigured model fails loudly instead of appearing to work. See translation.md §9 for the
/// tracked decision on adding SigV4 (either hand-rolled signing or by delegating to
/// <c>AWSSDK.BedrockRuntime</c>).
/// </para>
/// </summary>
public sealed class BedrockChatClient : IChatClient
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly string _region;
    private readonly string _modelId;
    private readonly string _apiKey;

    public BedrockChatClient(ModelConfig config, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        _modelId = (config.Model ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(_modelId))
        {
            throw new InvalidOperationException($"Bedrock model config '{config.Id}' has an empty model id.");
        }

        _apiKey = (config.ApiKey ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(_apiKey))
        {
            throw new InvalidOperationException(
                $"Bedrock model config '{config.Id}' has no API key. Subconscious authenticates to " +
                "Bedrock with a Bedrock API key (bearer token); AWS SigV4 credential signing is not " +
                "implemented (see translation.md §9).");
        }

        if (!string.IsNullOrWhiteSpace(config.AwsAccessKeyId)
            || !string.IsNullOrWhiteSpace(config.AwsSecretAccessKey)
            || !string.IsNullOrWhiteSpace(config.AwsSessionToken))
        {
            throw new NotSupportedException(
                $"Bedrock model config '{config.Id}' supplies AWS access-key credentials, but SigV4 " +
                "request signing is not implemented — only Bedrock API keys (bearer tokens) are " +
                "supported. Failing loudly rather than ignoring the credentials. See translation.md §9.");
        }

        _region = BedrockEndpoints.ResolveRegion(config) ?? BedrockEndpoints.DefaultRegion;
        _http = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(messages, options, streaming: false);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, body);

        var text = BedrockConverseMapper.ExtractResponseText(body);
        var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            ModelId = _modelId,
            FinishReason = MapFinishReason(BedrockConverseMapper.ExtractStopReason(body)),
            Usage = BedrockConverseMapper.ExtractUsage(body),
        };
        return chatResponse;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = BuildRequest(messages, options, streaming: true);
        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            EnsureSuccess(response, errorBody);
        }

        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await foreach (var frame in AwsEventStreamDecoder
            .DecodeAsync(stream, cancellationToken)
            .ConfigureAwait(false))
        {
            // An "exception" message-type frame carries a modelled Bedrock error mid-stream.
            if (string.Equals(frame.MessageType, "exception", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Bedrock streaming error ({frame.EventType ?? "unknown"}): {frame.PayloadAsText}");
            }

            switch (frame.EventType)
            {
                case "contentBlockDelta":
                {
                    var delta = BedrockConverseMapper.ExtractDeltaText(frame.PayloadAsText);
                    if (!string.IsNullOrEmpty(delta))
                    {
                        yield return new ChatResponseUpdate(ChatRole.Assistant, delta) { ModelId = _modelId };
                    }
                    break;
                }
                case "messageStop":
                {
                    var stopReason = BedrockConverseMapper.ExtractStopReason(frame.PayloadAsText);
                    yield return new ChatResponseUpdate
                    {
                        Role = ChatRole.Assistant,
                        ModelId = _modelId,
                        FinishReason = MapFinishReason(stopReason),
                    };
                    break;
                }
                case "metadata":
                {
                    var usage = BedrockConverseMapper.ExtractUsage(frame.PayloadAsText);
                    if (usage is not null)
                    {
                        yield return new ChatResponseUpdate
                        {
                            Role = ChatRole.Assistant,
                            ModelId = _modelId,
                            Contents = [new UsageContent(usage)],
                        };
                    }
                    break;
                }
                // messageStart / contentBlockStart / contentBlockStop carry no text for our purposes.
            }
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    private HttpRequestMessage BuildRequest(
        IEnumerable<ChatMessage> messages, ChatOptions? options, bool streaming)
    {
        var body = BedrockConverseMapper.BuildRequest(messages, options).ToJsonString();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            BedrockEndpoints.ConverseUrl(_region, _modelId, streaming))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            streaming ? "application/vnd.amazon.eventstream" : "application/json"));
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }
        throw new HttpRequestException(
            $"Bedrock request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}",
            inner: null,
            statusCode: response.StatusCode);
    }

    /// <summary>Map Bedrock's <c>stopReason</c> vocabulary onto MEAI's <see cref="ChatFinishReason"/>.</summary>
    private static ChatFinishReason? MapFinishReason(string? stopReason) => stopReason switch
    {
        null => null,
        "end_turn" or "stop_sequence" => ChatFinishReason.Stop,
        "max_tokens" => ChatFinishReason.Length,
        "tool_use" => ChatFinishReason.ToolCalls,
        "content_filtered" or "guardrail_intervened" => ChatFinishReason.ContentFilter,
        _ => new ChatFinishReason(stopReason),
    };
}
