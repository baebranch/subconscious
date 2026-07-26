namespace Subconscious.Engine.Approval;

/// <summary>
/// Whether a tool reads/derives data with no side effects (<see cref="Query"/>) or
/// creates/updates/deletes data or otherwise has side effects (<see cref="Mutation"/>).
/// Mirrors the Python <c>QUERY</c>/<c>MUTATION</c> constants in <c>tools/__init__.py</c>.
/// </summary>
public enum OperationKind
{
    Query,
    Mutation,
}
