using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;
using System.IO;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Filesystem operations tool module. Provides file read/write/list operations.
/// Port of Python's <c>desktop_tools/filesystem.py</c>.
/// </summary>
public sealed class FilesystemToolModule : IToolModule
{
    public string Slug => "filesystem";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                ReadFile,
                "read_file",
                "Read the contents of a file. Returns the file contents as a string."),

            AIFunctionFactory.Create(
                WriteFile,
                "write_file",
                "Write content to a file. Creates the file if it doesn't exist, overwrites if it does."),

            AIFunctionFactory.Create(
                ListDirectory,
                "list_directory",
                "List files and directories in a directory. Returns a list of paths."),

            AIFunctionFactory.Create(
                CreateDirectory,
                "create_directory",
                "Create a new directory. Creates parent directories if they don't exist."),

            AIFunctionFactory.Create(
                DeleteFile,
                "delete_file",
                "Delete a file permanently."),

            AIFunctionFactory.Create(
                CopyFile,
                "copy_file",
                "Copy a file from source to destination."),

            AIFunctionFactory.Create(
                MoveFile,
                "move_file",
                "Move or rename a file or directory."),

            AIFunctionFactory.Create(
                GetFileInfo,
                "get_file_info",
                "Get detailed information about a file including size, creation time, and modification time.")
        ];
    }

    private static string ReadFile(
        [Description("Path to the file to read.")] string path,
        EngineContext context)
    {
        try
        {
            // Resolve relative paths against data dir if provided
            var resolvedPath = ResolvePath(path, context.DataDir);

            if (!File.Exists(resolvedPath))
            {
                return $"Error: File not found: '{path}'";
            }

            var content = File.ReadAllText(resolvedPath);
            return content.Length > 100_000
                ? $"File is too large ({content.Length} chars). Showing first 100,000:\n\n{content[..100_000]}[...]"
                : content;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied reading '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error reading '{path}': {ex.Message}";
        }
    }

    private static string WriteFile(
        [Description("Path to the file to write.")] string path,
        [Description("Content to write to the file.")] string content,
        EngineContext context)
    {
        try
        {
            var resolvedPath = ResolvePath(path, context.DataDir);
            var directory = Path.GetDirectoryName(resolvedPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(resolvedPath, content);
            return $"Successfully wrote {content.Length} characters to '{path}'";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied writing to '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error writing to '{path}': {ex.Message}";
        }
    }

    private static string ListDirectory(
        EngineContext context,
        [Description("Path to the directory to list. Defaults to current directory.")] string path = ".")
    {
        try
        {
            var resolvedPath = ResolvePath(path, context.DataDir);

            if (!Directory.Exists(resolvedPath))
            {
                return $"Error: Directory not found: '{path}'";
            }

            var entries = Directory.GetFileSystemEntries(resolvedPath);
            var output = new System.Text.StringBuilder($"Contents of '{path}':\n");

            foreach (var entry in entries)
            {
                var name = Path.GetFileName(entry);
                var isDirectory = Directory.Exists(entry);
                output.AppendLine($"  {(isDirectory ? "[DIR] " : "[FILE]")} {name}");
            }

            return output.ToString().TrimEnd();
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied listing '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error listing '{path}': {ex.Message}";
        }
    }

    private static string CreateDirectory(
        [Description("Path of the directory to create.")] string path,
        EngineContext context)
    {
        try
        {
            var resolvedPath = ResolvePath(path, context.DataDir);
            Directory.CreateDirectory(resolvedPath);
            return $"Successfully created directory: '{path}'";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied creating '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error creating '{path}': {ex.Message}";
        }
    }

    private static string DeleteFile(
        [Description("Path to the file to delete.")] string path,
        EngineContext context)
    {
        try
        {
            var resolvedPath = ResolvePath(path, context.DataDir);

            if (!File.Exists(resolvedPath))
            {
                return $"Error: File not found: '{path}'";
            }

            File.Delete(resolvedPath);
            return $"Successfully deleted: '{path}'";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied deleting '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error deleting '{path}': {ex.Message}";
        }
    }

    private static string CopyFile(
        [Description("Source file path.")] string sourcePath,
        [Description("Destination file path.")] string destinationPath,
        EngineContext context)
    {
        try
        {
            var resolvedSource = ResolvePath(sourcePath, context.DataDir);
            var resolvedDest = ResolvePath(destinationPath, context.DataDir);

            if (!File.Exists(resolvedSource))
            {
                return $"Error: Source file not found: '{sourcePath}'";
            }

            var destDirectory = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            File.Copy(resolvedSource, resolvedDest, overwrite: true);
            return $"Successfully copied '{sourcePath}' to '{destinationPath}'";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied during copy";
        }
        catch (IOException ex)
        {
            return $"Error copying '{sourcePath}' to '{destinationPath}': {ex.Message}";
        }
    }

    private static string MoveFile(
        [Description("Source path.")] string sourcePath,
        [Description("Destination path.")] string destinationPath,
        EngineContext context)
    {
        try
        {
            var resolvedSource = ResolvePath(sourcePath, context.DataDir);
            var resolvedDest = ResolvePath(destinationPath, context.DataDir);

            if (!Directory.Exists(resolvedSource) && !File.Exists(resolvedSource))
            {
                return $"Error: Source not found: '{sourcePath}'";
            }

            var destDirectory = Path.GetDirectoryName(resolvedDest);
            if (!string.IsNullOrEmpty(destDirectory) && !Directory.Exists(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            File.Move(resolvedSource, resolvedDest, overwrite: true);
            return $"Successfully moved '{sourcePath}' to '{destinationPath}'";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied during move";
        }
        catch (IOException ex)
        {
            return $"Error moving '{sourcePath}' to '{destinationPath}': {ex.Message}";
        }
    }

    private static string GetFileInfo(
        [Description("Path to the file.")] string path,
        EngineContext context)
    {
        try
        {
            var resolvedPath = ResolvePath(path, context.DataDir);

            if (!File.Exists(resolvedPath))
            {
                return $"Error: File not found: '{path}'";
            }

            var info = new FileInfo(resolvedPath);
            return $"""
                File: {path}
                Size: {info.Length:N0} bytes
                Created: {info.CreationTimeUtc:yyyy-MM-dd HH:mm:ss} UTC
                Modified: {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss} UTC
                Extension: {info.Extension}
                """;
        }
        catch (UnauthorizedAccessException)
        {
            return $"Error: Permission denied accessing '{path}'";
        }
        catch (IOException ex)
        {
            return $"Error accessing '{path}': {ex.Message}";
        }
    }

    private static string ResolvePath(string path, string dataDir)
    {
        if (Path.IsPathRooted(path))
        {
            return path;
        }

        if (!string.IsNullOrEmpty(dataDir))
        {
            return Path.Combine(dataDir, path);
        }

        return Path.GetFullPath(path);
    }
}
