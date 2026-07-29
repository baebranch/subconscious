using Microsoft.AspNetCore.Http;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Api.Events;
using Subconscious.Engine.Api.Services;
using Subconscious.Engine.Api.WebSocket;
using Subconscious.Engine.Dispatch;

namespace Subconscious.Engine.Api;

/// <summary>
/// ASP.NET Core HTTP + WebSocket endpoints for the engine's local API.
/// <para>
/// Port of Python's <c>api/app.py</c>. This is the single place every route is mapped —
/// REST CRUD (workspaces/threads/messages/models), the <c>/api/v1/events</c> WebSocket,
/// the <c>/api/v1/stream</c> SSE broadcast feed, and health — so the wire surface is easy
/// to audit against translation.md's compatibility contract in one file.
/// </para>
/// </summary>
public static class EngineMiddleware
{
    public static WebApplication MapEngineEndpoints(this WebApplication app)
    {
        // Health check — deliberately NOT behind bearer auth (BearerTokenMiddleware exempts
        // it) so discovery.ts's reachability probe never needs a token.
        app.MapGet("/api/v1/health", () => new { status = "ok", version = Constants.Version });

        MapWorkspaceEndpoints(app);
        MapThreadEndpoints(app);
        MapMessageEndpoints(app);
        MapModelEndpoints(app);

        // Engine-initiated broadcast feed (thread.created/thread.updated/message.created) —
        // the piece AG-UI's request/response run model can't express (translation.md §4.5).
        app.MapGet("/api/v1/stream", async (HttpContext context, IEventBus eventBus) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";

            await foreach (var @event in eventBus.SubscribeAsync(context.RequestAborted))
            {
                var json = System.Text.Json.JsonSerializer.Serialize(@event);
                await context.Response.WriteAsync($"data: {json}\n\n", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
            }
        });

        app.MapPost("/api/v1/agui", () => Results.Ok(new { message = "AG-UI endpoint not yet implemented" }));

        // The one bespoke WebSocket, multiplexing events/chat/tool-dispatch, per
        // translation.md's frozen compatibility contract for subconscious-code.
        app.Map("/api/v1/events", async (HttpContext context, WebSocketHandlerFactory factory) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var handler = factory.Create(socket);
            await handler.RunAsync();
        });

        return app;
    }

    private static void MapWorkspaceEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/workspaces", async (IWorkspaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllWorkspacesAsync(ct)));

        app.MapGet("/api/v1/workspaces/{uuid}", async (string uuid, IWorkspaceService svc, CancellationToken ct) =>
        {
            var workspace = await svc.GetWorkspaceByUuidAsync(uuid, ct);
            return workspace is null ? Results.NotFound() : Results.Ok(workspace);
        });

        app.MapPost("/api/v1/workspaces", async (CreateWorkspaceRequest request, IWorkspaceService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateWorkspaceAsync(request, ct)));

        app.MapPut("/api/v1/workspaces/{uuid}", async (string uuid, CreateWorkspaceRequest request, IWorkspaceService svc, CancellationToken ct) =>
        {
            var updated = await svc.UpdateWorkspaceAsync(uuid, request, ct);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        });

        app.MapDelete("/api/v1/workspaces/{uuid}", async (string uuid, IWorkspaceService svc, CancellationToken ct) =>
            await svc.DeleteWorkspaceAsync(uuid, ct) ? Results.NoContent() : Results.NotFound());

        // Threads scoped under a workspace, matching the TS client's
        // GET /workspaces/{uuid}/threads call.
        app.MapGet("/api/v1/workspaces/{uuid}/threads", async (string uuid, IThreadService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetThreadsAsync(uuid, ct)));
    }

    private static void MapThreadEndpoints(WebApplication app)
    {
        app.MapPost("/api/v1/threads", async (CreateThreadRequest request, IThreadService svc, IEventBus bus, CancellationToken ct) =>
        {
            var thread = await svc.CreateThreadAsync(request, ct);
            await bus.PublishAsync(new ThreadCreatedEvent
            {
                ThreadId = thread.Uuid,
                WorkspaceId = thread.WorkspaceUuid,
                Title = thread.Title ?? string.Empty,
            }, ct);
            return Results.Ok(thread);
        });

        app.MapGet("/api/v1/threads/{uuid}", async (string uuid, IThreadService svc, CancellationToken ct) =>
        {
            var thread = await svc.GetThreadByUuidAsync(uuid, ct);
            return thread is null ? Results.NotFound() : Results.Ok(thread);
        });

        app.MapPut("/api/v1/threads/{uuid}", async (string uuid, UpdateThreadRequest request, IThreadService svc, IEventBus bus, CancellationToken ct) =>
        {
            var updated = await svc.UpdateThreadAsync(uuid, request, ct);
            if (updated is null)
            {
                return Results.NotFound();
            }
            await bus.PublishAsync(new ThreadUpdatedEvent
            {
                ThreadId = updated.Uuid,
                Title = updated.Title,
                Description = updated.Description,
            }, ct);
            return Results.Ok(updated);
        });

        app.MapDelete("/api/v1/threads/{uuid}", async (string uuid, IThreadService svc, CancellationToken ct) =>
            await svc.DeleteThreadAsync(uuid, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapMessageEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/threads/{uuid}/messages", async (string uuid, IMessageService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetMessagesAsync(uuid, ct)));

        app.MapPost("/api/v1/messages", async (SendMessageRequest request, IMessageService svc, IEventBus bus, CancellationToken ct) =>
        {
            var message = await svc.CreateMessageAsync(request, ct);
            await bus.PublishAsync(new MessageCreatedEvent
            {
                MessageId = message.Uuid,
                ThreadId = message.ThreadUuid,
                Role = message.Role,
                Content = message.Content,
            }, ct);
            return Results.Ok(message);
        });
    }

    private static void MapModelEndpoints(WebApplication app)
    {
        // No model-config store exists yet (translation.md Phase 1 secrets store is still
        // open) — for now this always includes the Echo dev model so a client can complete
        // an end-to-end chat turn without any provider credentials configured.
        app.MapGet("/api/v1/models", () => Results.Ok(new[]
        {
            new ModelDto
            {
                Id = "echo",
                Name = "Echo (dev)",
                Provider = "subconscious",
                Description = "Echoes the last user message back; no credentials required.",
            },
        }));
    }
}
