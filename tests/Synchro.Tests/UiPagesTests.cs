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
    public void Inbox_RendersBranchDirtyCountAndLastOp()
    {
        var slave = _store.GetSlaves().Single(s => s.Name == "feature-ui");

        var page = UiPages.Inbox(slave);

        page.ShouldContain("Pending inbox · feature-ui");
        page.ShouldContain("<th>dirty files</th>");
        page.ShouldContain("pull at 08:04 utc");
    }

    [Fact]
    public void Conflicts_RendersThreeWayContentAndResolveForm()
    {
        var scene = _store.GetConflicts("feature-ui")!;

        var page = UiPages.Conflicts(scene);

        page.ShouldContain("<h4>theirs</h4>");
        page.ShouldContain("<h4>local</h4>");
        page.ShouldContain("<h4>base</h4>");
        page.ShouldContain("/slave/feature-ui/conflicts/resolve");
        page.ShouldContain(">drop</button>");
        page.ShouldContain(">replace</button>");
        page.ShouldContain(">merge</button>");
        page.ShouldContain("&quot;theirs: direct sync&quot;");
    }

    [Fact]
    public void Conflicts_ResolvedPathsDropTheirForms()
    {
        _store.Resolve("feature-ui", "src/Program.cs", "drop");
        var scene = _store.GetConflicts("feature-ui")!;

        var page = UiPages.Conflicts(scene);

        page.ShouldContain(">drop</span>");
    }
}
