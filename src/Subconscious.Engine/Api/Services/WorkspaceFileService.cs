using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Data;

namespace Subconscious.Engine.Api.Services;

/// <summary>Engine-owned, scope-checked file access for configured workspace roots.</summary>
public sealed class WorkspaceFileService : IWorkspaceFileService
{
    private const int MaxEditableFileBytes = 1_000_000;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".c", ".cc", ".cpp", ".cs", ".csx", ".css", ".fs", ".fsx", ".go", ".h", ".hpp",
        ".html", ".htm", ".java", ".js", ".jsx", ".json", ".md", ".markdown", ".php", ".ps1",
        ".py", ".rb", ".rs", ".sh", ".sql", ".toml", ".ts", ".tsx", ".txt", ".xml", ".xaml",
        ".yml", ".yaml",
    };

    private readonly SubconsciousDbContext _context;

    public WorkspaceFileService(SubconsciousDbContext context) =>
        _context = context ?? throw new ArgumentNullException(nameof(context));

    public async Task<IReadOnlyList<WorkspaceFileEntryDto>> ListAsync(string workspaceUuid, int rootIndex, string? relativePath, CancellationToken cancellationToken = default)
    {
        var root = await GetRootAsync(workspaceUuid, rootIndex, cancellationToken);
        var directory = ResolveExistingPath(root, relativePath, allowRoot: true);
        EnsureDirectory(directory);
        directory = ResolveExistingPath(root, relativePath, allowRoot: true); // Re-check before enumeration.
        EnsureDirectory(directory);

        var entries = new List<WorkspaceFileEntryDto>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.TopDirectoryOnly))
        {
            var attributes = GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
            entries.Add(new WorkspaceFileEntryDto
            {
                Name = Path.GetFileName(entry),
                RelativePath = ToRelativePath(root, entry),
                IsDirectory = (attributes & FileAttributes.Directory) != 0,
            });
        }

        return entries.OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<WorkspaceFileContentDto> ReadAsync(string workspaceUuid, int rootIndex, string relativePath, CancellationToken cancellationToken = default)
    {
        var root = await GetRootAsync(workspaceUuid, rootIndex, cancellationToken);
        var file = ResolveExistingFile(root, relativePath);
        if (new FileInfo(file).Length > MaxEditableFileBytes)
        {
            throw BadRequest($"The requested file exceeds the {MaxEditableFileBytes / 1_000_000} MB limit.");
        }

        file = ResolveExistingFile(root, relativePath); // Re-check scope and reparse points before I/O.
        return new WorkspaceFileContentDto { Content = await ReadUtf8Async(file, cancellationToken) };
    }

    public async Task<WorkspaceFileContentDto> WriteAsync(string workspaceUuid, int rootIndex, string relativePath, string? content, CancellationToken cancellationToken = default)
    {
        if (content is null) throw BadRequest("Content is required.");
        var bytes = Utf8.GetBytes(content);
        if (bytes.Length > MaxEditableFileBytes)
        {
            throw BadRequest($"Content exceeds the {MaxEditableFileBytes / 1_000_000} MB limit.");
        }

        var root = await GetRootAsync(workspaceUuid, rootIndex, cancellationToken);
        _ = ResolveExistingFile(root, relativePath);
        var file = ResolveExistingFile(root, relativePath); // Re-check scope and reparse points before I/O.
        await WriteUtf8Async(file, bytes, cancellationToken);
        return new WorkspaceFileContentDto { Content = content };
    }

    public async Task<WorkspaceFileContentDto> CreateAsync(string workspaceUuid, int rootIndex, string relativePath, string? content, CancellationToken cancellationToken = default)
    {
        if (content is null) throw BadRequest("Content is required.");
        var bytes = Utf8.GetBytes(content);
        if (bytes.Length > MaxEditableFileBytes)
        {
            throw BadRequest($"Content exceeds the {MaxEditableFileBytes / 1_000_000} MB limit.");
        }

        var root = await GetRootAsync(workspaceUuid, rootIndex, cancellationToken);
        _ = ResolveNewFile(root, relativePath);
        var file = ResolveNewFile(root, relativePath); // Re-check parent, scope, and reparse points immediately before create.
        try
        {
            await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        catch (IOException) when (File.Exists(file) || Directory.Exists(file))
        {
            throw Conflict("A file or directory already exists at the requested path.");
        }

        return new WorkspaceFileContentDto { Content = content };
    }

    public async Task<WorkspaceFileEntryDto> CreateDirectoryAsync(
        string workspaceUuid, int rootIndex, string relativePath, CancellationToken cancellationToken = default)
    {
        var root = await GetRootAsync(workspaceUuid, rootIndex, cancellationToken);
        var directory = ResolveNewDirectory(root, relativePath);
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (IOException) when (File.Exists(directory) || Directory.Exists(directory))
        {
            throw Conflict("A file or directory already exists at the requested path.");
        }

        return new WorkspaceFileEntryDto
        {
            Name = Path.GetFileName(directory),
            RelativePath = ToRelativePath(root, directory),
            IsDirectory = true,
        };
    }

    private async Task<string> GetRootAsync(string workspaceUuid, int rootIndex, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceUuid)) throw BadRequest("Workspace UUID is required.");
        var workspace = await _context.Workspaces.AsNoTracking()
            .Where(candidate => candidate.Uuid == workspaceUuid)
            .Select(candidate => new { candidate.Directories })
            .FirstOrDefaultAsync(cancellationToken);
        if (workspace is null) throw NotFound("The requested workspace does not exist.");

        var roots = ParseRoots(workspace.Directories);
        if (rootIndex < 0 || rootIndex >= roots.Count) throw BadRequest("rootIndex does not select a configured workspace root.");
        var configuredRoot = roots[rootIndex];
        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
        {
            throw BadRequest("The selected workspace root is not an absolute path.");
        }

        string root;
        try { root = Path.GetFullPath(configuredRoot); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw BadRequest("The selected workspace root is invalid.");
        }
        EnsureDirectory(root);
        if ((GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw Forbidden("The selected workspace root is a reparse point.");
        }
        return root;
    }

    private static List<string?> ParseRoots(string? rawDirectories)
    {
        if (string.IsNullOrWhiteSpace(rawDirectories)) return [];
        try { return JsonSerializer.Deserialize<List<string?>>(rawDirectories) ?? []; }
        catch (JsonException) { throw BadRequest("Workspace directories must be a JSON array of absolute paths."); }
    }

    private static string ResolveExistingFile(string root, string? relativePath)
    {
        var file = ResolveExistingPath(root, relativePath, allowRoot: false);
        var attributes = GetAttributes(file);
        if ((attributes & FileAttributes.Directory) != 0) throw BadRequest("The content path must identify a file, not a directory.");
        if (!SupportedExtensions.Contains(Path.GetExtension(file)))
        {
            throw BadRequest("The requested file is not a supported text or source file.");
        }
        return file;
    }

    private static string ResolveNewDirectory(string root, string? relativePath)
    {
        if (relativePath is null) throw BadRequest("A workspace-relative directory path is required.");
        relativePath = relativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (relativePath.Length == 0) throw BadRequest("The workspace-relative path must not be empty.");

        try
        {
            if (Path.IsPathRooted(relativePath) || Path.IsPathFullyQualified(relativePath)
                || relativePath.IndexOf(Path.VolumeSeparatorChar) >= 0)
            {
                throw BadRequest("The path must be workspace-relative.");
            }
            var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
            {
                throw BadRequest("The path must not contain traversal segments.");
            }

            var directory = Path.GetFullPath(Path.Combine(root, relativePath));
            var parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent)) throw BadRequest("The workspace-relative path is invalid.");
            parent = VerifyExistingPath(root, parent);
            EnsureDirectory(parent);
            EnsureNewTargetIsNotReparsePoint(directory);
            if (File.Exists(directory) || Directory.Exists(directory))
            {
                throw Conflict("A file or directory already exists at the requested path.");
            }
            return directory;
        }
        catch (WorkspaceFileServiceException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw BadRequest("The workspace-relative path is invalid.");
        }
    }

    private static string ResolveNewFile(string root, string? relativePath)
    {
        if (relativePath is null) throw BadRequest("A workspace-relative file path is required.");
        if (relativePath.Length == 0) throw BadRequest("The workspace-relative path must not be empty.");

        try
        {
            if (Path.IsPathRooted(relativePath) || Path.IsPathFullyQualified(relativePath)
                || relativePath.IndexOf(Path.VolumeSeparatorChar) >= 0)
            {
                throw BadRequest("The path must be workspace-relative.");
            }
            var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
            {
                throw BadRequest("The path must not contain traversal segments.");
            }

            var file = Path.GetFullPath(Path.Combine(root, relativePath));
            var parent = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(parent)) throw BadRequest("The workspace-relative path is invalid.");
            parent = VerifyExistingPath(root, parent);
            EnsureDirectory(parent);
            EnsureNewTargetIsNotReparsePoint(file);
            if (!SupportedExtensions.Contains(Path.GetExtension(file)))
            {
                throw BadRequest("The requested file is not a supported text or source file.");
            }
            return file;
        }
        catch (WorkspaceFileServiceException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw BadRequest("The workspace-relative path is invalid.");
        }
    }

    private static void EnsureNewTargetIsNotReparsePoint(string path)
    {
        try
        {
            if ((GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw Forbidden("The requested path contains a reparse point.");
            }
        }
        catch (WorkspaceFileServiceException exception) when (exception.StatusCode == 404)
        {
            // A missing target is expected; its existing parent was validated separately.
        }
    }

    private static string ResolveExistingPath(string root, string? relativePath, bool allowRoot)
    {
        if (relativePath is null)
        {
            if (allowRoot) return VerifyExistingPath(root, root);
            throw BadRequest("A workspace-relative file path is required.");
        }
        if (relativePath.Length == 0) throw BadRequest("The workspace-relative path must not be empty.");

        try
        {
            if (Path.IsPathRooted(relativePath) || Path.IsPathFullyQualified(relativePath)
                || relativePath.IndexOf(Path.VolumeSeparatorChar) >= 0)
            {
                throw BadRequest("The path must be workspace-relative.");
            }
            var segments = relativePath.Split(['/', '\\'], StringSplitOptions.None);
            if (segments.Any(segment => segment is "." or ".."))
            {
                throw BadRequest("The path must not contain traversal segments.");
            }
            return VerifyExistingPath(root, Path.GetFullPath(Path.Combine(root, relativePath)));
        }
        catch (WorkspaceFileServiceException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw BadRequest("The workspace-relative path is invalid.");
        }
    }

    private static string VerifyExistingPath(string root, string target)
    {
        if (!IsWithinRoot(root, target)) throw Forbidden("The requested path is outside the configured workspace root.");
        if ((GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw Forbidden("The selected workspace root is a reparse point.");
        }

        var relative = Path.GetRelativePath(root, target);
        var current = root;
        if (relative.Length == 0) return current;
        foreach (var component in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            var attributes = GetAttributes(current);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw Forbidden("The requested path contains a reparse point.");
            }
        }
        return current;
    }

    private static void EnsureDirectory(string path)
    {
        var attributes = GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0) throw BadRequest("The requested path must identify a directory.");
    }

    private static bool IsWithinRoot(string root, string target)
    {
        var relative = Path.GetRelativePath(root, target);
        return relative != ".." && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static async Task<string> ReadUtf8Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxEditableFileBytes) throw BadRequest($"The requested file exceeds the {MaxEditableFileBytes / 1_000_000} MB limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var bytes = buffer.ToArray();
        var offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        try { return Utf8.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) { throw BadRequest("The requested file is not valid UTF-8 text."); }
    }

    private static async Task WriteUtf8Async(string path, byte[] content, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous);
        stream.SetLength(0);
        await stream.WriteAsync(content, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static FileAttributes GetAttributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (UnauthorizedAccessException) { throw Forbidden("Access to the workspace path was denied."); }
        catch (FileNotFoundException) { throw NotFound("The requested workspace path does not exist."); }
        catch (DirectoryNotFoundException) { throw NotFound("The requested workspace path does not exist."); }
        catch (IOException) { throw Forbidden("The workspace path cannot be accessed."); }
    }

    private static WorkspaceFileServiceException BadRequest(string message) => new(400, message);
    private static WorkspaceFileServiceException Forbidden(string message) => new(403, message);
    private static WorkspaceFileServiceException NotFound(string message) => new(404, message);
    private static WorkspaceFileServiceException Conflict(string message) => new(409, message);
}
