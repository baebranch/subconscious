using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Api.Services;

/// <summary>Implementation of thread service with EF Core.</summary>
public class ThreadService : IThreadService
{
    private readonly SubconsciousDbContext _context;

    public ThreadService(SubconsciousDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<List<ThreadDto>> GetThreadsAsync(string workspaceUuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == workspaceUuid, cancellationToken);
        if (workspace is null) return [];
        var threads = await _context.Threads.Where(thread => thread.WorkspaceId == workspace.Id)
            .OrderByDescending(thread => thread.UpdatedAt).ToListAsync(cancellationToken);
        return threads.Select(thread => MapToDto(thread, workspace)).ToList();
    }

    public async Task<ThreadDto?> GetThreadByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.Include(thread => thread.Workspace)
            .FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        return thread is null ? null : MapToDto(thread, thread.Workspace!);
    }

    public async Task<ThreadDto> CreateThreadAsync(CreateThreadRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == request.WorkspaceUuid, cancellationToken);
        if (workspace is null) throw new InvalidOperationException($"Workspace '{request.WorkspaceUuid}' not found");
        var thread = new Data.Entities.Thread
        {
            Uuid = Guid.NewGuid().ToString(), WorkspaceId = workspace.Id, Title = request.Title,
            Description = request.Description, DefaultModelId = request.DefaultModelId,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _context.Threads.Add(thread);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(thread, workspace);
    }

    public async Task<ThreadDto?> UpdateThreadAsync(string uuid, UpdateThreadRequest request, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.Include(thread => thread.Workspace)
            .FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        if (thread is null) return null;
        if (request.Title is not null) thread.Title = request.Title;
        if (request.Description is not null) thread.Description = request.Description;
        if (request.DefaultModelId is not null) thread.DefaultModelId = request.DefaultModelId;
        thread.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(thread, thread.Workspace!);
    }

    public async Task<ToolConfigDto?> GetToolsConfigAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.Include(thread => thread.Workspace)
            .AsNoTracking().FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        return thread is null ? null : new ToolConfigDto
        {
            Config = ToolConfigJson.ResolveNode(thread.Workspace!.ToolsConfig, thread.ToolsConfig),
        };
    }

    public async Task<ToolConfigDto?> UpdateToolsConfigAsync(string uuid, UpdateToolConfigRequest request, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.Include(thread => thread.Workspace)
            .FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        if (thread is null) return null;
        var desired = ToolConfigJson.RequireObject(request.Config);
        var baseline = ToolConfigJson.ResolveNode(thread.Workspace!.ToolsConfig, null) as System.Text.Json.Nodes.JsonObject;
        var delta = ToolConfigJson.Delta(baseline, desired);
        thread.ToolsConfig = delta is null ? null : ToolConfigJson.Serialize(delta);
        thread.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return new ToolConfigDto { Config = ToolConfigJson.ResolveNode(thread.Workspace.ToolsConfig, thread.ToolsConfig) };
    }

    public async Task<bool> ResetToolsConfigAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        if (thread is null) return false;
        thread.ToolsConfig = null;
        thread.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteThreadAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads.FirstOrDefaultAsync(thread => thread.Uuid == uuid, cancellationToken);
        if (thread is null) return false;
        _context.Threads.Remove(thread);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ThreadDto MapToDto(Data.Entities.Thread thread, Workspace workspace) => new()
    {
        Id = thread.Id, Uuid = thread.Uuid, WorkspaceUuid = workspace.Uuid, Title = thread.Title,
        Description = thread.Description, DefaultModelId = thread.DefaultModelId,
        ToolsConfig = ToolConfigJson.Resolve(workspace.ToolsConfig, thread.ToolsConfig),
        CreatedAt = thread.CreatedAt, UpdatedAt = thread.UpdatedAt,
    };
}
