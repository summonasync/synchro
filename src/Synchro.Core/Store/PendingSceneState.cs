namespace Synchro.Core.Store;

public sealed record PendingSceneState
{
    public required string PackagePath { get; init; }
    public required string Digest { get; init; }
    public required string SceneDir { get; init; }
    public required IReadOnlyList<string> ConflictPaths { get; init; }
    public required string NewPosition { get; init; }
    public required string? BasePosition { get; init; }
    public Dictionary<string, string> Resolutions { get; init; } = [];
}
