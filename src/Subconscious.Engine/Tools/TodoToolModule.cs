using Microsoft.Extensions.AI;
using Subconscious.Engine.Data.Entities;
using System.ComponentModel;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Provides todo/task management tools that work with the workspace database.
/// </summary>
public class TodoToolModule : IToolModule
{
    public string Slug => "todo";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        var tools = new List<AIFunction>();

        // add_todo
        tools.Add(AIFunctionFactory.Create(
            ([Description("Short description of the task (required)")] string title,
             [Description("Optional longer description or context")] string notes = "",
             [Description("One of 'low', 'normal', 'high', 'urgent' (default 'normal')")] string priority = "normal",
             [Description("Optional ISO date string 'YYYY-MM-DD' or 'YYYY-MM-DD HH:MM'")] string? due_date = null) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var todo = new TodoItem
                {
                    WorkspaceId = workspaceId,
                    ThreadId = context.ThreadId > 0 ? (int?)context.ThreadId : null,
                    Title = title,
                    Notes = notes ?? "",
                    Priority = priority,
                    Status = "open",
                    DueDate = string.IsNullOrEmpty(due_date) ? null : DateTime.Parse(due_date),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.Database!.TodoItems.Add(todo);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    todo = new
                    {
                        id = todo.Id,
                        title = todo.Title,
                        notes = todo.Notes,
                        priority = todo.Priority,
                        status = todo.Status,
                        due_date = todo.DueDate?.ToString("yyyy-MM-dd")
                    }
                };
            },
            name: "add_todo",
            description: "Create a new to-do item in the current workspace."));

        // list_todos
        tools.Add(AIFunctionFactory.Create(
            ([Description("Filter by status (optional, returns all statuses if omitted)")] string? status = null,
             [Description("Filter by priority (optional)")] string? priority = null) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var query = context.Database!.TodoItems
                    .Where(t => t.WorkspaceId == workspaceId);

                if (!string.IsNullOrEmpty(status))
                    query = query.Where(t => t.Status == status);

                if (!string.IsNullOrEmpty(priority))
                    query = query.Where(t => t.Priority == priority);

                var todos = query
                    .OrderByDescending(t => t.CreatedAt)
                    .Select(t => new
                    {
                        id = t.Id,
                        title = t.Title,
                        notes = t.Notes,
                        priority = t.Priority,
                        status = t.Status,
                        due_date = t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : null,
                        created_at = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                    })
                    .ToList();

                return (object)new { todos };
            },
            name: "list_todos",
            description: "List to-do items in the current workspace. Optionally filter by status or priority."));

        // update_todo
        tools.Add(AIFunctionFactory.Create(
            ([Description("The numeric ID of the to-do item")] int todo_id,
             [Description("New title (optional)")] string? title = null,
             [Description("New notes (optional)")] string? notes = null,
             [Description("New status: 'open', 'in_progress', 'done', 'cancelled' (optional)")] string? status = null,
             [Description("New priority: 'low', 'normal', 'high', 'urgent' (optional)")] string? priority = null,
             [Description("New due date 'YYYY-MM-DD' (optional, pass empty string to clear)")] string? due_date = null) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var todo = context.Database!.TodoItems
                    .FirstOrDefault(t => t.Id == todo_id && t.WorkspaceId == workspaceId);

                if (todo == null)
                    return (object)new { status = "error", message = "Todo not found" };

                if (!string.IsNullOrEmpty(title)) todo.Title = title;
                if (notes != null) todo.Notes = notes;
                if (!string.IsNullOrEmpty(status)) todo.Status = status;
                if (!string.IsNullOrEmpty(priority)) todo.Priority = priority;
                if (due_date != null)
                {
                    todo.DueDate = string.IsNullOrEmpty(due_date) ? null : DateTime.Parse(due_date);
                }

                todo.UpdatedAt = DateTime.UtcNow;
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    todo = new
                    {
                        id = todo.Id,
                        title = todo.Title,
                        notes = todo.Notes,
                        priority = todo.Priority,
                        status = todo.Status,
                        due_date = todo.DueDate?.ToString("yyyy-MM-dd")
                    }
                };
            },
            name: "update_todo",
            description: "Update one or more fields of an existing to-do item."));

        // complete_todo
        tools.Add(AIFunctionFactory.Create(
            ([Description("The numeric ID of the to-do item")] int todo_id) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var todo = context.Database!.TodoItems
                    .FirstOrDefault(t => t.Id == todo_id && t.WorkspaceId == workspaceId);

                if (todo == null)
                    return (object)new { status = "error", message = "Todo not found" };

                todo.Status = "done";
                todo.UpdatedAt = DateTime.UtcNow;
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    message = $"Todo #{todo_id} marked as done"
                };
            },
            name: "complete_todo",
            description: "Mark a to-do item as done."));

        // delete_todo
        tools.Add(AIFunctionFactory.Create(
            ([Description("The numeric ID of the to-do item to delete")] int todo_id) =>
            {
                var workspaceId = (int)context.WorkspaceId;
                var todo = context.Database!.TodoItems
                    .FirstOrDefault(t => t.Id == todo_id && t.WorkspaceId == workspaceId);

                if (todo == null)
                    return (object)new { status = "error", message = "Todo not found" };

                context.Database.TodoItems.Remove(todo);
                context.Database.SaveChanges();

                return (object)new
                {
                    status = "success",
                    message = $"Todo #{todo_id} deleted"
                };
            },
            name: "delete_todo",
            description: "Permanently delete a to-do item by ID."));

        return tools;
    }
}
