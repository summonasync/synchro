using System.Text.RegularExpressions;
using Shouldly;
using Synchro.App.Ui;
using Xunit;

namespace Synchro.Tests;

public class UiPageTests
{
    [Fact]
    public void Html_FetchTargets_LockReadOnlyApiSurface()
    {
        var allowed = new[] { "/api/status", "/api/log", "/api/scene", "/api/scene/read" };

        var fetchTargets = Regex.Matches(UiPage.Html, "fetch\\('([^']+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        fetchTargets.ShouldNotBeEmpty();
        fetchTargets.ShouldAllBe(f => allowed.Contains(f));
    }

    [Fact]
    public void Html_CarriesTheProductIdentity()
    {
        UiPage.Html.ShouldContain("SYNCHRO");
        UiPage.Html.ShouldContain("/api/status");
        UiPage.Html.ShouldContain("setInterval");
    }
}
