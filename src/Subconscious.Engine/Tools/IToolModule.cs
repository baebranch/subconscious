using Microsoft.Extensions.AI;

namespace Subconscious.Engine.Tools;

/// <summary>
/// A group of related built-in tools registered under a single slug ("time", "calculator",
/// "todo", ...). The .NET analog of a Python tool module exposing a module-level
/// <c>TOOLS = [...]</c> list, which <c>BaseToolRegistry._load_base_tools</c> collected by slug.
///
/// <para>
/// Tools are created per <see cref="EngineContext"/> rather than being static, because the
/// context replaces pydantic-ai's injected <c>RunContext</c> — see
/// <see cref="EngineContext"/> remarks for why.
/// </para>
/// </summary>
public interface IToolModule
{
    /// <summary>Registry slug this module's tools are grouped under (e.g. "time").</summary>
    string Slug { get; }

    /// <summary>
    /// Build this module's tools, closing over <paramref name="context"/>. Implementations must
    /// not touch <see cref="EngineContext.Database"/> during creation — the catalog is built
    /// with <see cref="EngineContext.ForCatalog"/>, which has none.
    /// </summary>
    IReadOnlyList<AIFunction> CreateTools(EngineContext context);
}
