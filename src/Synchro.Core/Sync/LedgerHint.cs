using System.Text.Json;
using System.Text.Json.Serialization;

namespace Synchro.Core.Sync;

public sealed record LedgerHint
{
    public required HistoryRelation Relation { get; init; }

    public required string? MergeBase { get; init; }

    public required string MasterHead { get; init; }

    public required string SlaveHead { get; init; }

    public required string? SlaveSyncHead { get; init; }

    public required IReadOnlyList<string> ConsumedShas { get; init; }

    public required IReadOnlyList<string> PendingShas { get; init; }

    public required bool DeSync { get; init; }

    public static string ToJson(LedgerHint hint) =>
        JsonSerializer.Serialize(hint, Options);

    public static LedgerHint? Parse(string json) =>
        JsonSerializer.Deserialize<LedgerHint>(json, Options);

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
