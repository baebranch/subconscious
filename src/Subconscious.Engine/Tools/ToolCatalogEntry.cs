namespace Subconscious.Engine.Tools;

/// <summary>
/// One entry in the tool catalog the UI renders as a toggle tree. Mirrors the dict produced by
/// <c>BaseToolRegistry.catalog()</c>: <c>{"name": ..., "doc": &lt;first docstring line&gt;}</c>.
/// </summary>
/// <param name="Name">Tool name as the model sees it (snake_case, matching the Python callables).</param>
/// <param name="Doc">
/// One-line summary. Python took the first line of the callable's docstring; here it is the
/// first line of the tool's description, which is authored from the same source material.
/// </param>
/// <param name="Operation">
/// Query/mutation classification from <see cref="Approval.OperationClassifier"/>. Not present in
/// the Python catalog — added because the UI already needs it to show which tools are
/// approval-gated, and deriving it here avoids the client reimplementing the classifier.
/// </param>
public sealed record ToolCatalogEntry(
    string Name,
    string Doc,
    Approval.OperationKind Operation);
