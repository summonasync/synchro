using Shouldly;
using Synchro.App.Ui;
using Xunit;

namespace Synchro.Tests;

public class FixtureUiStoreTests
{
    private readonly FixtureUiStore _store = new();

    [Fact]
    public void GetSlaves_ExposesEverySlaveCard()
    {
        var slaves = _store.GetSlaves();

        slaves.Count.ShouldBe(3);
        slaves.Select(s => s.Name).ShouldBe(["feature-ui", "docs-mirror", "scratch-lab"]);
        slaves.ShouldAllBe(s => s.SyncBranch.Length > 0);
        slaves.ShouldAllBe(s => s.BaseBranch.Length > 0);
    }

    [Fact]
    public void GetHistory_ReturnsNewestFirst_AndEmptyForUnknownSlave()
    {
        var history = _store.GetHistory("feature-ui");

        history.ShouldNotBeEmpty();
        for (var i = 1; i < history.Count; i++)
        {
            history[i - 1].AtUtc.ShouldBeGreaterThanOrEqualTo(history[i].AtUtc);
        }

        _store.GetHistory("nope").ShouldBeEmpty();
    }

    [Fact]
    public void GetConflicts_ReturnsThreeWayScene_OrNullForUnknownSlave()
    {
        var scene = _store.GetConflicts("feature-ui");

        scene.ShouldNotBeNull();
        scene.Paths.Count.ShouldBe(3);
        scene.Paths.ShouldAllBe(p => p.Resolution == "unresolved");

        _store.GetConflicts("nope").ShouldBeNull();
    }
}
