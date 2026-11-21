namespace NoticeForge.Domain;

/// <summary>A license or notice text that was found. Origin records where it came from (nupkg, the file's URL on GitHub at that commit, or config).</summary>
public sealed record TextFile(string Name, string Origin, string Content);
