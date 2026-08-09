namespace Subconscious.Engine.Api.DTOs;

/// <summary>A file or directory contained in a configured workspace root.</summary>
public record WorkspaceFileEntryDto
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required bool IsDirectory { get; init; }
}

/// <summary>UTF-8 text content read from a workspace file.</summary>
public record WorkspaceFileContentDto
{
    public required string Content { get; init; }
}

/// <summary>UTF-8 text content to write to an existing workspace file.</summary>
public record WriteWorkspaceFileRequest
{
    public string? Content { get; init; }
}
