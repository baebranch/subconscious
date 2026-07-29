using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Registry for database-backed tool modules (todo, memory, notes, contacts, knowledge).
/// Requires a SubconsciousDbContext instance via EngineContext.
/// </summary>
public class DatabaseToolRegistry
{
    private readonly EngineContext _context;
    private readonly List<IToolModule> _modules;

    public DatabaseToolRegistry(EngineContext context)
    {
        _context = context;
        _modules = new List<IToolModule>
        {
            new TodoToolModule(),
            new MemoryToolModule(),
            new NotesToolModule(),
            new ContactsToolModule(),
            new KnowledgeToolModule()
        };
    }

    /// <summary>
    /// Get all tools from database-backed modules.
    /// </summary>
    public IEnumerable<AIFunction> GetAllTools()
    {
        return _modules.SelectMany(m => m.CreateTools(_context));
    }

    /// <summary>
    /// Get tools from a specific module by name.
    /// </summary>
    public IEnumerable<AIFunction> GetToolsFromModule(string moduleName)
    {
        IToolModule? module = moduleName.ToLowerInvariant() switch
        {
            "todo" => _modules.OfType<TodoToolModule>().FirstOrDefault(),
            "memory" => _modules.OfType<MemoryToolModule>().FirstOrDefault(),
            "notes" => _modules.OfType<NotesToolModule>().FirstOrDefault(),
            "contacts" => _modules.OfType<ContactsToolModule>().FirstOrDefault(),
            "knowledge" => _modules.OfType<KnowledgeToolModule>().FirstOrDefault(),
            _ => null
        };

        return module?.CreateTools(_context) ?? Enumerable.Empty<AIFunction>();
    }

    /// <summary>
    /// Get all available tool names.
    /// </summary>
    public IEnumerable<string> GetToolNames()
    {
        return GetAllTools().Select(t => t.Name);
    }
}
