using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using System.ComponentModel;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Tool module providing persistent key-value memory capabilities.
/// </summary>
public class MemoryToolModule : IToolModule
{
    public string Slug => "memory";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(
                ([Description("Short identifier for the fact, e.g. 'user_name'.")] string key,
                 [Description("The value to store, e.g. 'Alice'.")] string value) =>
                {
                    if (context.Database == null)
                        throw new InvalidOperationException("Database context is not available.");

                    var workspaceId = (int)context.WorkspaceId;
                    var memory = context.Database.WorkspaceMemories.FirstOrDefault(m => 
                        m.WorkspaceId == workspaceId && m.Key == key);

                    if (memory == null)
                    {
                        memory = new WorkspaceMemory
                        {
                            WorkspaceId = workspaceId,
                            Key = key,
                            Value = value,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        context.Database.WorkspaceMemories.Add(memory);
                    }
                    else
                    {
                        memory.Value = value;
                        memory.UpdatedAt = DateTime.UtcNow;
                    }

                    context.Database.SaveChanges();
                    return $"Remembered {key} = {value}";
                },
                "remember",
                "Store a fact in workspace memory under a descriptive key. If a value already exists for this key it will be overwritten."),

            AIFunctionFactory.Create(
                ([Description("The key to look up, e.g. 'user_name'.")] string key) =>
                {
                    if (context.Database == null)
                        throw new InvalidOperationException("Database context is not available.");

                    var workspaceId = (int)context.WorkspaceId;
                    var memory = context.Database.WorkspaceMemories.FirstOrDefault(m => 
                        m.WorkspaceId == workspaceId && m.Key == key);

                    if (memory == null)
                        return $"No memory found for key '{key}'";

                    return memory.Value;
                },
                "recall",
                "Retrieve a previously stored fact by its key."),

            AIFunctionFactory.Create(
                () =>
                {
                    if (context.Database == null)
                        throw new InvalidOperationException("Database context is not available.");

                    var workspaceId = (int)context.WorkspaceId;
                    var memories = context.Database.WorkspaceMemories
                        .Where(m => m.WorkspaceId == workspaceId)
                        .OrderBy(m => m.Key)
                        .Select(m => new { m.Key, m.Value })
                        .ToList();

                    if (!memories.Any())
                        return "No memories stored.";

                    return string.Join("\n", memories.Select(m => $"{m.Key} = {m.Value}"));
                },
                "list_memories",
                "Return all facts stored in the current workspace's memory."),

            AIFunctionFactory.Create(
                ([Description("The key of the memory to remove.")] string key) =>
                {
                    if (context.Database == null)
                        throw new InvalidOperationException("Database context is not available.");

                    var workspaceId = (int)context.WorkspaceId;
                    var memory = context.Database.WorkspaceMemories.FirstOrDefault(m => 
                        m.WorkspaceId == workspaceId && m.Key == key);

                    if (memory == null)
                        return $"No memory found for key '{key}'";

                    context.Database.WorkspaceMemories.Remove(memory);
                    context.Database.SaveChanges();
                    return $"Forgot {key}";
                },
                "forget",
                "Delete a stored memory entry by its key."),

            AIFunctionFactory.Create(
                () =>
                {
                    if (context.Database == null)
                        throw new InvalidOperationException("Database context is not available.");

                    var workspaceId = (int)context.WorkspaceId;
                    var memories = context.Database.WorkspaceMemories
                        .Where(m => m.WorkspaceId == workspaceId)
                        .ToList();

                    var count = memories.Count;
                    context.Database.WorkspaceMemories.RemoveRange(memories);
                    context.Database.SaveChanges();

                    return $"Cleared {count} memories from workspace.";
                },
                "forget_all",
                "Clear all memory for the current workspace. This is irreversible — use with caution.")
        };

        return tools;
    }
}
