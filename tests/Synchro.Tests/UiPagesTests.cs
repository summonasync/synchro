using Shouldly;
using Synchro.App.Ui;
using Xunit;

namespace Synchro.Tests;

public class UiPagesTests
{
    private readonly FixtureUiStore _store = new();

    [Fact]
    public void Dashboard_RendersSlaveCardsAndNavigation()
    {
        var page = UiPages.Dashboard(_store);

        page.ShouldContain("<h1>SYNCHRO</h1>");
        page.ShouldContain("feature-ui");
        page.ShouldContain("~/repos/synchro-slave-ui");
        page.ShouldContain("feature-sync");
        page.ShouldContain("docs-mirror");
        page.ShouldContain("scratch-lab");
        page.ShouldContain("/slave/feature-ui/history");
        page.ShouldContain("/slave/feature-ui/inbox");
        page.ShouldContain("/slave/feature-ui/conflicts");
        page.ShouldContain("pack console");
        page.ShouldContain("sync in flight");
    }

    [Fact]
    public void History_RendersEventRowsNewestFirst()
    {
        var page = UiPages.History("feature-ui", _store.GetHistory("feature-ui"));

        page.ShouldContain("Sync history · feature-ui");
        page.ShouldContain("<th>direction</th>");
        page.ShouldContain("master-wins on Readme.md");
    }

    [Fact]
    public void History_EmptyLogFallsBackToWarning()
    {
        var page = UiPages.History("docs-mirror", []);

        page.ShouldContain("no sync events yet");
    }

    [Fact]
    public void Inbox_RendersBothInboxesAndLastOp()
    {
        var slave = _store.GetSlaves().Single(s => s.Name == "feature-ui");

        var page = UiPages.Inbox(slave);

        page.ShouldContain("Pending inbox · feature-ui");
        page.ShouldContain("<th>slave inbox</th>");
        page.ShouldContain("<th>master inbox</th>");
        page.ShouldContain("2026-11-20 08:04:00Z master → slave");
    }

    [Fact]
    public void Conflicts_HistoryRendersResolutionPills_WithoutAnswerForms()
    {
        var scene = _store.GetConflicts("feature-ui")!;

        var page = UiPages.Conflicts("feature-ui", scene, new UiSyncRunner("/nonexistent"));

        page.ShouldContain("<h3>src/Program.cs<span class=\"pill resolved\">unresolved</span></h3>");
        page.ShouldNotContain("conflicts/answer");
        page.ShouldNotContain("<h4>master</h4>");
    }

    [Fact]
    public void Conflicts_EmptyHistoryFallsBackToAllClear()
    {
        var page = UiPages.Conflicts("docs-mirror", null, new UiSyncRunner("/nonexistent"));

        page.ShouldContain("no conflicts recorded");
    }
}
