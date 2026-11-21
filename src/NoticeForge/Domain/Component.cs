namespace NoticeForge.Domain;

/// <summary>Kind of bundled component.</summary>
public enum ComponentKind
{
    /// <summary>A NuGet package.</summary>
    Package,
    /// <summary>Listed in deps.json as a runtimepack, such as a self-contained .NET runtime. Read from the NuGet folder the same way.</summary>
    RuntimePack,
    /// <summary>Outside NuGet (written by a human in config extras).</summary>
    Extra,
}

/// <summary>
/// A dependency. For notices, something bundled in the distribution; for check, something some project references.
/// UsedBy is, for check, which projects use it (where their lock files are).
/// </summary>
public sealed record Component(string Id, string Version, ComponentKind Kind, IReadOnlyList<string>? UsedBy = null);
