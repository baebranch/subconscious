using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;

namespace Subconscious.Engine.Tools;

/// <summary>
/// Knowledge/RAG tool module - provides document search and knowledge graph capabilities.
/// </summary>
public class KnowledgeToolModule : IToolModule
{
    public string Slug => "knowledge";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        var tools = new List<AIFunction>
        {
            AIFunctionFactory.Create(
                async ([Description("Natural language search query")] string query,
                       [Description("Number of results to return (1-20)")] int limit = 6) =>
                {
                    if (context.Database == null)
                        return (object)"Database not available";

                    limit = Math.Clamp(limit, 1, 20);
                    var workspaceId = (int)context.WorkspaceId;

                    // Basic search - find documents with matching content in chunks
                    var results = await context.Database.DocumentChunks
                        .Include(c => c.Document)
                        .Where(c => c.WorkspaceId == workspaceId &&
                                   c.Content.Contains(query))
                        .OrderByDescending(c => c.Content.Length) // Simple relevance
                        .Take(limit)
                        .Select(c => new
                        {
                            Path = c.Document!.Path,
                            c.Content,
                            c.StartLine,
                            c.EndLine
                        })
                        .ToListAsync();

                    if (results.Count == 0)
                        return (object)"No matching documents found";

                    var output = string.Join("\n\n", results.Select(r =>
                        $"File: {r.Path} (lines {r.StartLine}-{r.EndLine})\n{r.Content}"));

                    return (object)output;
                },
                "search_knowledge",
                "Search indexed documents for relevant passages"),

            AIFunctionFactory.Create(
                async ([Description("Natural language investigation query")] string query,
                       [Description("Number of seed passages (1-20)")] int limit = 6) =>
                {
                    if (context.Database == null)
                        return (object)"Database not available";

                    limit = Math.Clamp(limit, 1, 20);
                    var workspaceId = (int)context.WorkspaceId;

                    // Basic graph search - find related chunks from same document
                    var seedChunks = await context.Database.DocumentChunks
                        .Include(c => c.Document)
                        .Where(c => c.WorkspaceId == workspaceId &&
                                   c.Content.Contains(query))
                        .Take(limit / 2)
                        .ToListAsync();

                    if (seedChunks.Count == 0)
                        return (object)"No matching passages found";

                    // Get related chunks from same documents
                    var documentIds = seedChunks.Select(c => c.DocumentId).Distinct().ToList();
                    var relatedChunks = await context.Database.DocumentChunks
                        .Include(c => c.Document)
                        .Where(c => documentIds.Contains(c.DocumentId) &&
                                   !seedChunks.Select(s => s.Id).Contains(c.Id))
                        .Take(limit / 2)
                        .ToListAsync();

                    var allChunks = seedChunks.Concat(relatedChunks);
                    var output = string.Join("\n\n", allChunks.Select(c =>
                        $"File: {c.Document!.Path} (lines {c.StartLine}-{c.EndLine})\n{c.Content}"));

                    return (object)output;
                },
                "search_knowledge_graph",
                "Search knowledge graph for related passages")
        };

        return tools;
    }
}
