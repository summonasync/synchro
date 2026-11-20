namespace Synchro.App.Ui;

internal sealed class FixtureUiStore : IUiStore
{
    private static readonly DateTimeOffset Base = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private readonly List<SlaveView> _slaves;
    private readonly Dictionary<string, List<HistoryEntryView>> _history = new();
    private readonly Dictionary<string, List<ConflictPathView>> _conflicts = new(StringComparer.Ordinal);

    public FixtureUiStore()
    {
        _slaves =
        [
            new("feature-ui", "~/repos/synchro-slave-ui", "feature-sync", "main",
                "in sync · 14 events",
                new InboxView("pending", "feature-sync", 3, null),
                new InboxView("open", "feature-sync", 0, null),
                false, "2026-11-20 08:04:00Z master → slave — 3 file(s), no conflicts"),
            new("docs-mirror", "~/repos/synchro-slave-docs", "docs-sync", "main",
                "in sync · 6 events",
                new InboxView("clean", "main", 0, null),
                new InboxView("clean", "main", 0, null),
                false, "2026-11-20 07:41:00Z slave → master — 4 file(s), no conflicts"),
            new("scratch-lab", "~/repos/synchro-slave-lab", "feature-sync", "develop",
                "re-syncing · 2 events",
                new InboxView("pending", "feature-sync", 1, null),
                new InboxView("missing", null, 0, null),
                true, "2026-11-20 08:11:00Z slave → master — 2 file(s), 1 conflict(s): lab/settings.json (slave wins)"),
        ];

        _history["feature-ui"] =
        [
            new(Base.AddMinutes(4), "pull", 3, null),
            new(Base.AddMinutes(-9), "push", 1, null),
            new(Base.AddMinutes(-31), "pull", 2, "master-wins on Readme.md"),
            new(Base.AddMinutes(-58), "push", 5, null),
        ];
        _history["docs-mirror"] =
        [
            new(Base.AddMinutes(-19), "push", 4, null),
            new(Base.AddMinutes(-47), "pull", 1, null),
        ];
        _history["scratch-lab"] =
        [
            new(Base.AddMinutes(11), "pull", 1, null),
            new(Base.AddMinutes(-22), "pull", 2, "slave-wins on lab/settings.json"),
        ];

        _conflicts["feature-ui"] =
        [
            new("src/Program.cs", "unresolved",
                "Console.WriteLine(\"theirs: direct sync\");",
                "Console.WriteLine(\"local: watchdog tweak\");",
                "Console.WriteLine(\"hello\");"),
            new("docs/flow.md", "unresolved",
                "# Flow (master)\n\nDirect repo-to-repo sync over local paths.",
                "# Flow (slave)\n\nDirect sync with inbox notes.",
                "# Flow\n\nHand-carried packages only."),
            new("skill/SYNCHRO.md", "unresolved",
                "sync rules from master\n",
                "sync rules with slave annotations\n",
                "sync rules\n"),
        ];
    }

    public IReadOnlyList<SlaveView> GetSlaves() => _slaves;

    public IReadOnlyList<HistoryEntryView> GetHistory(string slaveName) =>
        _history.GetValueOrDefault(slaveName, []);

    public SlaveConflictView? GetConflicts(string slaveName) =>
        _conflicts.TryGetValue(slaveName, out var paths)
            ? new SlaveConflictView(slaveName, paths)
            : null;
}
