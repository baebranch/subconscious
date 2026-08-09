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
        MapToolEndpoints(app);
        MapMessageEndpoints(app);
        MapModelEndpoints(app);
        MapModelConfigurationEndpoints(app);
        MapSettingsEndpoints(app);

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

        MapWorkspaceFileEndpoints(app);

        app.MapGet("/api/v1/workspaces/{uuid}/tools-config", async (string uuid, IWorkspaceService svc, CancellationToken ct) =>
        {
            var config = await svc.GetToolsConfigAsync(uuid, ct);
            return config is null ? Results.NotFound() : Results.Ok(config);
        });
        app.MapPut("/api/v1/workspaces/{uuid}/tools-config", async (string uuid, UpdateToolConfigRequest request, IWorkspaceService svc, CancellationToken ct) =>
        {
            try
            {
                var config = await svc.UpdateToolsConfigAsync(uuid, request, ct);
                return config is null ? Results.NotFound() : Results.Ok(config);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });

        // Threads scoped under a workspace, matching the TS client's GET /workspaces/{uuid}/threads call.
        app.MapGet("/api/v1/workspaces/{uuid}/threads", async (string uuid, IThreadService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetThreadsAsync(uuid, ct)));
    }

    private static void MapWorkspaceFileEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/workspaces/{uuid}/files", async (string uuid, int rootIndex, string? path, IWorkspaceFileService svc, CancellationToken ct) =>
        {
            try
            {
                return (IResult)Results.Ok(await svc.ListAsync(uuid, rootIndex, path, ct));
            }
            catch (WorkspaceFileServiceException exception) { return WorkspaceFileFailure(exception); }
            catch (UnauthorizedAccessException) { return WorkspaceFileForbidden("Access to the workspace path was denied."); }
            catch (IOException exception) { return WorkspaceFileIoFailure(exception); }
        });

        app.MapGet("/api/v1/workspaces/{uuid}/files/content", async (string uuid, int rootIndex, string? path, IWorkspaceFileService svc, CancellationToken ct) =>
        {
            try
            {
                return (IResult)Results.Ok(await svc.ReadAsync(uuid, rootIndex, path ?? string.Empty, ct));
            }
            catch (WorkspaceFileServiceException exception) { return WorkspaceFileFailure(exception); }
            catch (UnauthorizedAccessException) { return WorkspaceFileForbidden("Access to the workspace path was denied."); }
            catch (IOException exception) { return WorkspaceFileIoFailure(exception); }
        });

        app.MapPost("/api/v1/workspaces/{uuid}/files/content", async (string uuid, int rootIndex, string? path, WriteWorkspaceFileRequest request, IWorkspaceFileService svc, CancellationToken ct) =>
        {
            try
            {
                return (IResult)Results.Ok(await svc.CreateAsync(uuid, rootIndex, path ?? string.Empty, request.Content, ct));
            }
            catch (WorkspaceFileServiceException exception) { return WorkspaceFileFailure(exception); }
            catch (System.Text.EncoderFallbackException) { return WorkspaceFileBadRequest("Content is not valid UTF-8 text."); }
            catch (UnauthorizedAccessException) { return WorkspaceFileForbidden("Access to the workspace path was denied."); }
            catch (IOException exception) { return WorkspaceFileIoFailure(exception); }
        });

        app.MapPut("/api/v1/workspaces/{uuid}/files/content", async (string uuid, int rootIndex, string? path, WriteWorkspaceFileRequest request, IWorkspaceFileService svc, CancellationToken ct) =>
        {
            try
            {
                return (IResult)Results.Ok(await svc.WriteAsync(uuid, rootIndex, path ?? string.Empty, request.Content, ct));
            }
            catch (WorkspaceFileServiceException exception) { return WorkspaceFileFailure(exception); }
            catch (System.Text.EncoderFallbackException) { return WorkspaceFileBadRequest("Content is not valid UTF-8 text."); }
            catch (UnauthorizedAccessException) { return WorkspaceFileForbidden("Access to the workspace path was denied."); }
            catch (IOException exception) { return WorkspaceFileIoFailure(exception); }
        });
    }

    private static IResult WorkspaceFileFailure(WorkspaceFileServiceException exception) =>
        Results.Json(new { error = exception.Message }, statusCode: exception.StatusCode);

    private static IResult WorkspaceFileBadRequest(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult WorkspaceFileForbidden(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult WorkspaceFileNotFound(string message) =>
        Results.Json(new { error = message }, statusCode: StatusCodes.Status404NotFound);

    private static IResult WorkspaceFileIoFailure(IOException exception) => exception is FileNotFoundException or DirectoryNotFoundException
        ? WorkspaceFileNotFound("The requested workspace path does not exist.")
        : WorkspaceFileForbidden("The workspace path cannot be accessed.");

    private static void MapThreadEndpoints(WebApplication app)
    {
        app.MapPost("/api/v1/threads", async (CreateThreadRequest request, IThreadService svc, IEventBus bus, CancellationToken ct) =>
        {
            var thread = await svc.CreateThreadAsync(request, ct);
            await bus.PublishAsync(new ThreadCreatedEvent { ThreadId = thread.Uuid, WorkspaceId = thread.WorkspaceUuid, Title = thread.Title ?? string.Empty }, ct);
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
            if (updated is null) return Results.NotFound();
            await bus.PublishAsync(new ThreadUpdatedEvent { ThreadId = updated.Uuid, Title = updated.Title, Description = updated.Description }, ct);
            return Results.Ok(updated);
        });
        app.MapDelete("/api/v1/threads/{uuid}", async (string uuid, IThreadService svc, CancellationToken ct) =>
            await svc.DeleteThreadAsync(uuid, ct) ? Results.NoContent() : Results.NotFound());

        app.MapGet("/api/v1/threads/{uuid}/tools-config", async (string uuid, IThreadService svc, CancellationToken ct) =>
        {
            var config = await svc.GetToolsConfigAsync(uuid, ct);
            return config is null ? Results.NotFound() : Results.Ok(config);
        });
        app.MapPut("/api/v1/threads/{uuid}/tools-config", async (string uuid, UpdateToolConfigRequest request, IThreadService svc, CancellationToken ct) =>
        {
            try
            {
                var config = await svc.UpdateToolsConfigAsync(uuid, request, ct);
                return config is null ? Results.NotFound() : Results.Ok(config);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        app.MapDelete("/api/v1/threads/{uuid}/tools-config", async (string uuid, IThreadService svc, CancellationToken ct) =>
            await svc.ResetToolsConfigAsync(uuid, ct) ? Results.NoContent() : Results.NotFound());
    }

    private static void MapToolEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/tools/catalog", async (IToolRegistryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetCatalogAsync(ct)));
        app.MapGet("/api/v1/tool-registry", async (IToolRegistryService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetAllAsync(ct)));
        app.MapGet("/api/v1/tool-registry/{uuid}", async (string uuid, IToolRegistryService svc, CancellationToken ct) =>
        {
            var tool = await svc.GetByUuidAsync(uuid, ct);
            return tool is null ? Results.NotFound() : Results.Ok(tool);
        });
        app.MapPost("/api/v1/tool-registry", async (UpsertToolRegistryRequest request, IToolRegistryService svc, CancellationToken ct) =>
        {
            try
            {
                var created = await svc.CreateAsync(request, ct);
                return Results.Created($"/api/v1/tool-registry/{Uri.EscapeDataString(created.Uuid)}", created);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        app.MapPut("/api/v1/tool-registry/{uuid}", async (string uuid, UpsertToolRegistryRequest request, IToolRegistryService svc, CancellationToken ct) =>
        {
            try
            {
                var updated = await svc.UpdateAsync(uuid, request, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            }
            catch (ArgumentException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
        app.MapDelete("/api/v1/tool-registry/{uuid}", async (string uuid, IToolRegistryService svc, CancellationToken ct) =>
            await svc.DeleteAsync(uuid, ct) ? Results.NoContent() : Results.NotFound());
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

    /// <summary>Exposes the generic <c>app_state</c> table. Clients scope their settings with
    /// <c>tag</c> and <c>client</c>; individual client models own value validation.</summary>
    private static void MapSettingsEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/settings", async (
            string? key, string? tag, string? client, SubconsciousDbContext db, CancellationToken ct) =>
        {
            var query = db.AppState.AsNoTracking();
            if (key is not null)
            {
                query = query.Where(setting => setting.Key == key);
            }
            if (tag is not null)
            {
                query = query.Where(setting => setting.Tag == tag);
            }
            if (client is not null)
            {
                query = query.Where(setting => setting.Client == client);
            }

            var settings = await query.OrderBy(setting => setting.Id)
                .Select(setting => new AppStateSettingDto
                {
                    Key = setting.Key,
                    Value = setting.Value,
                    Tag = setting.Tag,
                    Client = setting.Client,
                })
                .ToListAsync(ct);
            return Results.Ok(settings);
        });

        app.MapPut("/api/v1/settings", async (
            IReadOnlyList<AppStateSettingDto> settings, SubconsciousDbContext db, CancellationToken ct) =>
        {
            if (settings.Count == 0
                || settings.Any(setting => string.IsNullOrWhiteSpace(setting.Key) || setting.Value is null)
                || settings.GroupBy(setting => new { setting.Key, setting.Tag, setting.Client }).Any(group => group.Count() > 1))
            {
                return Results.BadRequest(new { error = "Settings must have a key, value, and unique key/tag/client scope." });
            }

            foreach (var setting in settings)
            {
                var state = await db.AppState.SingleOrDefaultAsync(existing =>
                    existing.Key == setting.Key
                    && existing.Tag == setting.Tag
                    && existing.Client == setting.Client, ct);
                if (state is null)
                {
                    db.AppState.Add(new AppState
                    {
                        Key = setting.Key,
                        Value = setting.Value,
                        Tag = setting.Tag,
                        Client = setting.Client,
                    });
                }
                else
                {
                    state.Value = setting.Value;
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(settings);
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
