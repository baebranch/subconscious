using Subconscious.Engine.Api.DTOs;

namespace Subconscious.Engine.Api.Services;

/// <summary>Provides bounded browsing and editing of configured workspace roots.</summary>
public interface IWorkspaceFileService
{
    Task<IReadOnlyList<WorkspaceFileEntryDto>> ListAsync(string workspaceUuid, int rootIndex, string? relativePath, CancellationToken cancellationToken = default);
    Task<WorkspaceFileContentDto> ReadAsync(string workspaceUuid, int rootIndex, string relativePath, CancellationToken cancellationToken = default);
    Task<WorkspaceFileContentDto> WriteAsync(string workspaceUuid, int rootIndex, string relativePath, string? content, CancellationToken cancellationToken = default);
    Task<WorkspaceFileContentDto> CreateAsync(string workspaceUuid, int rootIndex, string relativePath, string? content, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceFileServiceException : Exception
{
    public WorkspaceFileServiceException(int statusCode, string message) : base(message) => StatusCode = statusCode;
    public int StatusCode { get; }
}
