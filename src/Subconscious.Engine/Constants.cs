using System.Reflection;

namespace Subconscious.Engine;

/// <summary>
/// Engine-wide constants. Mirrors <c>constants.py</c> (<c>VERSION</c>) from the Python
/// implementation, but sources the version from the assembly's informational version
/// (set via <c>Directory.Build.props</c> / csproj <c>Version</c>) instead of a runtime
/// package-metadata lookup.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Human-readable engine version string, e.g. "v0.1.0". Matches the "v"-prefixed
    /// format returned by the Python <c>VERSION</c> constant.
    /// </summary>
    public static string Version { get; } = "v" + (
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? "0.0.0");
}
