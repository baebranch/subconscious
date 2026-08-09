using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Api.Services;

/// <summary>Implementation of workspace service with EF Core.</summary>
public class WorkspaceService : IWorkspaceService
{
    private readonly SubconsciousDbContext _context;

    public WorkspaceService(SubconsciousDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<List<WorkspaceDto>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default) =>
        (await _context.Workspaces.OrderBy(workspace => workspace.Name).ToListAsync(cancellationToken)).Select(MapToDto).ToList();

    public async Task<WorkspaceDto?> GetWorkspaceByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == uuid, cancellationToken);
        return workspace is null ? null : MapToDto(workspace);
    }

    public async Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var network = await _context.Networks.FirstOrDefaultAsync(cancellationToken) ?? await CreateDefaultNetworkAsync(cancellationToken);
        var workspace = new Workspace
        {
            Uuid = Guid.NewGuid().ToString(), Name = request.Name, Description = request.Description,
            NetworkId = network.Id, DefaultModelId = request.DefaultModelId, ToolsConfig = request.ToolsConfig,
            Directories = request.Directories, ApprovalConfig = request.ApprovalConfig, RagConfig = request.RagConfig,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _context.Workspaces.Add(workspace);
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(workspace);
    }

    public async Task<WorkspaceDto?> UpdateWorkspaceAsync(string uuid, CreateWorkspaceRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == uuid, cancellationToken);
        if (workspace is null) return null;
        workspace.Name = request.Name;
        workspace.Description = request.Description;
        workspace.DefaultModelId = request.DefaultModelId;
        workspace.ToolsConfig = request.ToolsConfig;
        workspace.Directories = request.Directories;
        workspace.ApprovalConfig = request.ApprovalConfig;
        workspace.RagConfig = request.RagConfig;
        workspace.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return MapToDto(workspace);
    }

    public async Task<ToolConfigDto?> GetToolsConfigAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.AsNoTracking().FirstOrDefaultAsync(workspace => workspace.Uuid == uuid, cancellationToken);
        return workspace is null ? null : new ToolConfigDto { Config = ToolConfigJson.Parse(workspace.ToolsConfig) };
    }

    public async Task<ToolConfigDto?> UpdateToolsConfigAsync(string uuid, UpdateToolConfigRequest request, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == uuid, cancellationToken);
        if (workspace is null) return null;
        var config = ToolConfigJson.RequireObject(request.Config);
        workspace.ToolsConfig = ToolConfigJson.Serialize(config);
        workspace.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return new ToolConfigDto { Config = config };
    }

    public async Task<bool> DeleteWorkspaceAsync(string uuid, CancellationToken cancellationToken = default)
    {
        var workspace = await _context.Workspaces.FirstOrDefaultAsync(workspace => workspace.Uuid == uuid, cancellationToken);
        if (workspace is null) return false;
        _context.Workspaces.Remove(workspace);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<Network> CreateDefaultNetworkAsync(CancellationToken cancellationToken)
    {
        var network = new Network { Uuid = Guid.NewGuid().ToString(), Name = "default", Description = "Default network", CreatedAt = DateTime.UtcNow };
        _context.Networks.Add(network);
        await _context.SaveChangesAsync(cancellationToken);
        return network;
    }

    private static WorkspaceDto MapToDto(Workspace workspace) => new()
    {
        Id = workspace.Id, Uuid = workspace.Uuid, Name = workspace.Name, Description = workspace.Description,
        DefaultModelId = workspace.DefaultModelId, ToolsConfig = workspace.ToolsConfig, Directories = workspace.Directories,
        ApprovalConfig = workspace.ApprovalConfig, RagConfig = workspace.RagConfig,
        CreatedAt = workspace.CreatedAt, UpdatedAt = workspace.UpdatedAt,
    };
}
