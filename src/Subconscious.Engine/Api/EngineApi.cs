using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Configuration;
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
        MapModelConfigurationEndpoints(app);
        MapPanelConfigurationEndpoints(app);

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

    private static readonly HashSet<string> ValidPanelConfigurations =
    [
        "ContextChatMain",
        "ChatContextMain",
        "ContextMainChat",
        "ChatMainContext",
        "MainContextChat",
        "MainChatContext",
    ];

    private static void MapPanelConfigurationEndpoints(WebApplication app)
    {
        const string key = "panel_configuration";
        const string tag = "ui_state";
        const string defaultConfiguration = "ContextChatMain";

        app.MapGet("/api/v1/settings/panel-configuration", async (SubconsciousDbContext db, CancellationToken ct) =>
        {
            var configuration = await db.AppState
                .Where(state => state.Key == key && state.Tag == tag)
                .Select(state => state.Value)
                .SingleOrDefaultAsync(ct);

            return Results.Ok(new PanelConfigurationDto
            {
                Configuration = configuration ?? defaultConfiguration,
            });
        });

        app.MapPut("/api/v1/settings/panel-configuration", async (
            UpdatePanelConfigurationRequest request, SubconsciousDbContext db, CancellationToken ct) =>
        {
            if (!ValidPanelConfigurations.Contains(request.Configuration))
            {
                return Results.BadRequest(new { error = "Unsupported panel configuration." });
            }

            var state = await db.AppState.SingleOrDefaultAsync(item => item.Key == key && item.Tag == tag, ct);
            if (state is null)
            {
                db.AppState.Add(new AppState { Key = key, Tag = tag, Value = request.Configuration });
            }
            else
            {
                state.Value = request.Configuration;
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new PanelConfigurationDto { Configuration = request.Configuration });
        });
    }

    private static void MapModelConfigurationEndpoints(WebApplication app)
    {
        // Model credentials remain in data.enc. These bearer-authenticated routes return only
        // redacted metadata; ApiKey is accepted solely by POST/PUT and never serialized back.
        app.MapGet("/api/v1/model-configurations", async (IModelConfigurationStore store, CancellationToken ct) =>
        {
            try
            {
                return (IResult)Results.Ok(await store.ListAsync(ct));
            }
            catch (ModelConfigurationStoreException exception)
            {
                return StorageProblem(exception);
            }
        });

        app.MapPost("/api/v1/model-configurations", async (UpsertModelConfigurationRequest request, IModelConfigurationStore store, CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateAsync(request, ct);
                return (IResult)Results.Created($"/api/v1/model-configurations/{Uri.EscapeDataString(created.Id)}", created);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (ModelConfigurationStoreException exception)
            {
                return StorageProblem(exception);
            }
        });

        app.MapPut("/api/v1/model-configurations/{id}", async (string id, UpsertModelConfigurationRequest request, IModelConfigurationStore store, CancellationToken ct) =>
        {
            try
            {
                var updated = await store.UpdateAsync(id, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (ModelConfigurationStoreException exception)
            {
                return StorageProblem(exception);
            }
        });

        app.MapDelete("/api/v1/model-configurations/{id}", async (string id, IModelConfigurationStore store, CancellationToken ct) =>
        {
            try
            {
                return await store.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound();
            }
            catch (ModelConfigurationStoreException exception)
            {
                return StorageProblem(exception);
            }
        });
    }

    private static IResult StorageProblem(ModelConfigurationStoreException exception) =>
        Results.Problem(
            detail: exception.Message,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Encrypted model configuration is unavailable.");

    private static void MapModelEndpoints(WebApplication app)
    {
        // This catalog keeps the credential-free Echo model available for development chat. User
        // model settings are exposed through /model-configurations so secrets cannot leak into
        // callers that only need chat model metadata.
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
