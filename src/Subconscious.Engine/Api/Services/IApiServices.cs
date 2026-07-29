using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data.Entities;

namespace Subconscious.Engine.Api.Services;

/// <summary>
/// Service interface for workspace operations.
/// </summary>
public interface IWorkspaceService
{
    Task<List<WorkspaceDto>> GetAllWorkspacesAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceDto?> GetWorkspaceByUuidAsync(string uuid, CancellationToken cancellationToken = default);
    Task<WorkspaceDto> CreateWorkspaceAsync(CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<WorkspaceDto?> UpdateWorkspaceAsync(string uuid, CreateWorkspaceRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteWorkspaceAsync(string uuid, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service interface for thread operations.
/// </summary>
public interface IThreadService
{
    Task<List<ThreadDto>> GetThreadsAsync(string workspaceUuid, CancellationToken cancellationToken = default);
    Task<ThreadDto?> GetThreadByUuidAsync(string uuid, CancellationToken cancellationToken = default);
    Task<ThreadDto> CreateThreadAsync(CreateThreadRequest request, CancellationToken cancellationToken = default);
    Task<ThreadDto?> UpdateThreadAsync(string uuid, UpdateThreadRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteThreadAsync(string uuid, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service interface for message operations.
/// </summary>
public interface IMessageService
{
    Task<List<MessageDto>> GetMessagesAsync(string threadUuid, CancellationToken cancellationToken = default);
    Task<MessageDto?> GetMessageByUuidAsync(string uuid, CancellationToken cancellationToken = default);
    Task<MessageDto> CreateMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
}
