using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace Subconscious.Engine.Api.Events;

/// <summary>
/// Event bus for publishing and subscribing to engine events.
/// <para>
/// Port of Python's <c>events.py</c>.
/// Uses Channels for thread-safe fan-out to multiple subscribers.
/// </para>
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publish an event to all subscribers.
    /// </summary>
    Task PublishAsync(object @event, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to events with an optional filter.
    /// </summary>
    /// <returns>An async enumerable of events.</returns>
    IAsyncEnumerable<object> SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribe to events of a specific type.
    /// </summary>
    IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Subscribe to events matching a predicate.
    /// </summary>
    IAsyncEnumerable<object> SubscribeAsync(Func<object, bool> predicate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of IEventBus using Channels.
/// </summary>
public sealed class EventBus : IEventBus, IDisposable
{
    private readonly Channel<object> _channel;
    private readonly List<Func<object, bool>> _filters = new();
    private readonly object _lock = new();

    public EventBus(int capacity = 100)
    {
        _channel = Channel.CreateBounded<object>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async Task PublishAsync(object @event, CancellationToken cancellationToken = default)
    {
        await _channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public IAsyncEnumerable<object> SubscribeAsync(CancellationToken cancellationToken = default)
    {
        return SubscribeAsync(_ => true, cancellationToken);
    }

    public IAsyncEnumerable<T> SubscribeAsync<T>(CancellationToken cancellationToken = default) where T : class
    {
        return SubscribeAsync(o => o is T, cancellationToken)
            .Select(o => (T)o);
    }

    public IAsyncEnumerable<object> SubscribeAsync(Func<object, bool> predicate, CancellationToken cancellationToken = default)
    {
        return Filter(predicate).Reader.ReadAllAsync(cancellationToken);
    }

    private Channel<object> Filter(Func<object, bool> predicate, CancellationToken cancellationToken = default)
    {
        var filterChannel = Channel.CreateBounded<object>(new BoundedChannelOptions(100)
        {
            FullMode = BoundedChannelFullMode.Wait
        });

        _ = Task.Run(async () =>
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var item))
                    {
                        if (predicate(item))
                        {
                            await filterChannel.Writer.WriteAsync(item).ConfigureAwait(false);
                        }
                    }
                }
            }
            finally
            {
                filterChannel.Writer.TryComplete();
            }
        }, cancellationToken);

        return filterChannel;
    }

    public void Dispose()
    {
        _channel.Writer.TryComplete();
    }
}

/// <summary>
/// Base event type for all engine events.
/// </summary>
public abstract record EngineEvent
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public string EventType => GetType().Name;
}

/// <summary>
/// Event published when a thread is created.
/// </summary>
public sealed record ThreadCreatedEvent : EngineEvent
{
    public required string ThreadId { get; init; }
    public required string WorkspaceId { get; init; }
    public required string Title { get; init; }
}

/// <summary>
/// Event published when a thread is updated.
/// </summary>
public sealed record ThreadUpdatedEvent : EngineEvent
{
    public required string ThreadId { get; init; }
    public string? Title { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// Event published when a message is created.
/// </summary>
public sealed record MessageCreatedEvent : EngineEvent
{
    public required string MessageId { get; init; }
    public required string ThreadId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
}

/// <summary>
/// Event published when a tool call is initiated.
/// </summary>
public sealed record ToolCallEvent : EngineEvent
{
    public required string ToolId { get; init; }
    public required string CorrelationId { get; init; }
    public JsonNode? Input { get; init; }
}

/// <summary>
/// Event published when a tool call completes.
/// </summary>
public sealed record ToolResultEvent : EngineEvent
{
    public required string ToolId { get; init; }
    public required string CorrelationId { get; init; }
    public JsonNode? Result { get; init; }
    public bool Success { get; init; }
}

/// <summary>
/// Event published for approval requests.
/// </summary>
public sealed record ApprovalRequestEvent : EngineEvent
{
    public required string ToolId { get; init; }
    public required string CorrelationId { get; init; }
    public required string ProviderId { get; init; }
}

/// <summary>
/// Event published for approval resolution.
/// </summary>
public sealed record ApprovalResolvedEvent : EngineEvent
{
    public required string ToolId { get; init; }
    public required string CorrelationId { get; init; }
    public required bool Approved { get; init; }
}
