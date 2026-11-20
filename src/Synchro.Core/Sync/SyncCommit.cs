namespace Synchro.Core.Sync;

public sealed record SyncCommit
{
    internal static readonly IReadOnlyDictionary<string, string> NoBlobs = new Dictionary<string, string>();

    public required string Sha { get; init; }

    public required string? PatchId { get; init; }

    public required IReadOnlyList<string> Files { get; init; }

    public IReadOnlyDictionary<string, string> PostBlobs { get; init; } = NoBlobs;

    public IReadOnlyDictionary<string, string> PreBlobs { get; init; } = NoBlobs;
}
