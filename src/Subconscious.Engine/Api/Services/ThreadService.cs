using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;

namespace Subconscious.Engine.Api.Services;

/// <summary>
/// Implementation of thread service with EF Core.
/// </summary>
public class ThreadService : IThreadService
{
    private readonly SubconsciousDbContext _context;

    public ThreadService(SubconsciousDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<ThreadDto>> GetThreadsAsync(string workspaceUuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Uuid == workspaceUuid, cancellationToken);

        if (workspace == null)
            return new List<ThreadDto>();

        var threads = await _context.Threads
            .Where(t => t.WorkspaceId == workspace.Id)
            .OrderByDescending(t => t.UpdatedAt)
            .ToListAsync(cancellationToken);

        return threads.Select(t => MapToDto(t, workspaceUuid)).ToList();
    }

    public async Task<ThreadDto?> GetThreadByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads
            .Include(t => t.Workspace)
            .FirstOrDefaultAsync(t => t.Uuid == uuid, cancellationToken);

        return thread == null ? null : MapToDto(thread, thread.Workspace!.Uuid);
    }

    public async Task<ThreadDto> CreateThreadAsync(CreateThreadRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Uuid == request.WorkspaceUuid, cancellationToken);

        if (workspace == null)
            throw new InvalidOperationException($"Workspace '{request.WorkspaceUuid}' not found");

        var thread = new Data.Entities.Thread
        {
            Uuid = Guid.NewGuid().ToString(),
            WorkspaceId = workspace.Id,
            Title = request.Title,
            Description = request.Description,
            DefaultModelId = request.DefaultModelId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Threads.Add(thread);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(thread, request.WorkspaceUuid);
    }

    public async Task<ThreadDto?> UpdateThreadAsync(string uuid, UpdateThreadRequest request, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads
            .Include(t => t.Workspace)
            .FirstOrDefaultAsync(t => t.Uuid == uuid, cancellationToken);

        if (thread == null)
            return null;

        if (request.Title != null)
            thread.Title = request.Title;
        if (request.Description != null)
            thread.Description = request.Description;
        if (request.DefaultModelId != null)
            thread.DefaultModelId = request.DefaultModelId;

        thread.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(thread, thread.Workspace!.Uuid);
    }

    public async Task<bool> DeleteThreadAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var thread = await _context.Threads
            .FirstOrDefaultAsync(t => t.Uuid == uuid, cancellationToken);

        if (thread == null)
            return false;

        _context.Threads.Remove(thread);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static ThreadDto MapToDto(Data.Entities.Thread thread, string workspaceUuid)
    {
        return new ThreadDto
        {
            Id = thread.Id,
            Uuid = thread.Uuid,
            WorkspaceUuid = workspaceUuid,
            Title = thread.Title,
            Description = thread.Description,
            DefaultModelId = thread.DefaultModelId,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt
        };
    }
}
