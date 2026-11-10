namespace Synchro.Core;

public sealed record SyncManifest
{
    public required int SchemaVersion { get; init; }
    public required string Channel { get; init; }
    public required SyncMode Mode { get; init; }
    public required Position? BasePosition { get; init; }
    public required Position NewPosition { get; init; }
    public required IReadOnlyList<FeatureDeclaration> Features { get; init; }
    public required IReadOnlyList<CommitInfo> Commits { get; init; }
    public required IReadOnlyList<PackageFileEntry> Files { get; init; }
    public required IReadOnlyList<string> Deletions { get; init; }
}

public sealed record FeatureDeclaration
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
}

public sealed record CommitInfo
{
    public required string Sha { get; init; }
    public required string Message { get; init; }
    public required IReadOnlyList<string> Files { get; init; }
}

public sealed record PackageFileEntry
{
    public required string Path { get; init; }
    public required string Hash { get; init; }
    public required byte[] Content { get; init; }
}
