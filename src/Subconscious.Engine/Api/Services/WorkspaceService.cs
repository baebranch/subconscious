using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Api.Services;

/// <summary>
/// Implementation of workspace service with EF Core.
/// </summary>
public class WorkspaceService : IWorkspaceService
{
    private readonly SubconsciousDbContext _context;

    public WorkspaceService(SubconsciousDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<List<WorkspaceDto>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        var workspaces = await _context.Workspaces
            .OrderBy(w => w.Name)
            .ToListAsync(cancellationToken);

        return workspaces.Select(MapToDto).ToList();
    }

    public async Task<WorkspaceDto?> GetWorkspaceByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Uuid == uuid, cancellationToken);

        return workspace == null ? null : MapToDto(workspace);
    }

    public async Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        // Get or create default network
        var network = await _context.Networks.FirstOrDefaultAsync(cancellationToken)
            ?? await CreateDefaultNetworkAsync(cancellationToken);

        var workspace = new Workspace
        {
            Uuid = Guid.NewGuid().ToString(),
            Name = request.Name,
            Description = request.Description,
            NetworkId = network.Id,
            DefaultModelId = request.DefaultModelId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Workspaces.Add(workspace);
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(workspace);
    }

    public async Task<WorkspaceDto?> UpdateWorkspaceAsync(string uuid, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Uuid == uuid, cancellationToken);

        if (workspace == null)
            return null;

        workspace.Name = request.Name;
        workspace.Description = request.Description;
        workspace.DefaultModelId = request.DefaultModelId;
        workspace.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(workspace);
    }

    public async Task<bool> DeleteWorkspaceAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces
            .FirstOrDefaultAsync(w => w.Uuid == uuid, cancellationToken);

        if (workspace == null)
            return false;

        _context.Workspaces.Remove(workspace);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task<Network> CreateDefaultNetworkAsync(CancellationToken cancellationToken)
    {
        var network = new Network
        {
            Uuid = Guid.NewGuid().ToString(),
            Name = "default",
            Description = "Default network",
            CreatedAt = DateTime.UtcNow
        };

        _context.Networks.Add(network);
        await _context.SaveChangesAsync(cancellationToken);

        return network;
    }

    private static WorkspaceDto MapToDto(Workspace workspace)
    {
        return new WorkspaceDto
        {
            Id = workspace.Id,
            Uuid = workspace.Uuid,
            Name = workspace.Name,
            Description = workspace.Description,
            DefaultModelId = workspace.DefaultModelId,
            CreatedAt = workspace.CreatedAt,
            UpdatedAt = workspace.UpdatedAt
        };
    }
}
