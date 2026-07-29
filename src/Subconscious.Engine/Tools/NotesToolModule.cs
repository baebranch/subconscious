using Microsoft.Extensions.AI;
using Subconscious.Engine.Data.Entities;
using System.ComponentModel;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Tool module for managing notes in the workspace database.
/// </summary>
public class NotesToolModule : IToolModule
{
    public string Slug => "notes";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        var tools = new List<AIFunction>();

        // save_note
        tools.Add(AIFunctionFactory.Create(
            ([Description("Short title for the note.")] string title,
             [Description("The body of the note (plain text or markdown).")] string content,
             [Description("Optional comma-separated tags, e.g. 'recipe, vegetarian'.")] string tags = "") =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var note = new Note
                {
                    WorkspaceId = workspaceId,
                    Title = title,
                    Content = content,
                    Tags = tags ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.Database!.Notes.Add(note);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    note = new
                    {
                        id = note.Id,
                        title = note.Title,
                        tags = note.Tags,
                        created_at = note.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    }
                };
            },
            name: "save_note",
            description: "Create a new note in the current workspace."));

        // list_notes
        tools.Add(AIFunctionFactory.Create(
            ([Description("If provided, only notes whose tags contain this string are returned.")] string? tag = null) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var query = context.Database!.Notes
                    .Where(n => n.WorkspaceId == workspaceId);

                if (!string.IsNullOrEmpty(tag) && tag != null)
                    query = query.Where(n => n.Tags != null && n.Tags.Contains(tag));

                var notes = query
                    .OrderByDescending(n => n.CreatedAt)
                    .Select(n => new
                    {
                        id = n.Id,
                        title = n.Title,
                        tags = n.Tags,
                        created_at = n.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                return (object)new { notes };
            },
            name: "list_notes",
            description: "List all notes in the current workspace. Optionally filter by a tag substring."));

        // get_note
        tools.Add(AIFunctionFactory.Create(
            ([Description("The numeric ID of the note.")] int note_id) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var note = context.Database!.Notes
                    .FirstOrDefault(n => n.Id == note_id && n.WorkspaceId == workspaceId);

                if (note == null)
                    return (object)new { status = "error", message = "Note not found" };

                return (object)new
                {
                    status = "success",
                    note = new
                    {
                        id = note.Id,
                        title = note.Title,
                        content = note.Content,
                        tags = note.Tags,
                        created_at = note.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        updated_at = note.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    }
                };
            },
            name: "get_note",
            description: "Retrieve the full content of a note by its ID."));

        // delete_note
        tools.Add(AIFunctionFactory.Create(
            ([Description("The numeric ID of the note to delete.")] int note_id) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var note = context.Database!.Notes
                    .FirstOrDefault(n => n.Id == note_id && n.WorkspaceId == workspaceId);

                if (note == null)
                    return (object)new { status = "error", message = "Note not found" };

                context.Database.Notes.Remove(note);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    message = $"Note #{note_id} deleted"
                };
            },
            name: "delete_note",
            description: "Permanently delete a note by its ID."));

        return tools;
    }
}
