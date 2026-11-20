using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Synchro.App.Ui;
using Synchro.Core;
using Synchro.Core.Store;
using Xunit;

namespace Synchro.Tests;

public class UiHostBindingTests
{
    [Fact]
    public async Task Host_OnFreePort_BindsLoopbackOnly()
    {
        var (app, _) = await StartHostAsync();
        try
        {
            var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
            addresses.ShouldNotBeEmpty();
            addresses.ShouldAllBe(a => a.StartsWith("http://127.0.0.1:"));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Host_ServesDashboardFromFixtureStore_And404sUnknownSlaves()
    {
        var (app, port) = await StartHostAsync();
        try
        {
            using var client = new HttpClient();

            using var homeResponse = await client.GetAsync($"http://127.0.0.1:{port}/");
            homeResponse.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
            var home = await homeResponse.Content.ReadAsStringAsync();
            home.ShouldContain("<h1>SYNCHRO</h1>");
            home.ShouldContain("feature-ui");

            using var missing = await client.GetAsync($"http://127.0.0.1:{port}/slave/nope/conflicts");
            missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Host_ConflictResolve_PostsDriveDropReplaceAndMerge()
    {
        var (app, port) = await StartHostAsync();
        try
        {
            using var client = new HttpClient();
            var baseUrl = $"http://127.0.0.1:{port}";

            await PostResolveAsync(client, baseUrl, "src/Program.cs", "drop");
            await PostResolveAsync(client, baseUrl, "docs/flow.md", "replace");
            await PostResolveAsync(client, baseUrl, "skill/SYNCHRO.md", "merge");

            var page = await client.GetStringAsync($"{baseUrl}/slave/feature-ui/conflicts");
            page.ShouldContain(">drop</span>");
            page.ShouldContain(">replace</span>");
            page.ShouldContain(">merge</span>");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void Runner_TryAnswerWithoutPendingReturnsFalse()
    {
        var runner = new UiSyncRunner(Path.GetTempPath());

        runner.TryAnswer("whatever", ConflictWinner.Master).ShouldBeFalse();
        runner.Pending.ShouldBeEmpty();
        runner.Running.ShouldBeFalse();
    }

    [Theory]
    [InlineData("master")]
    [InlineData("slave")]
    public async Task Host_UiSync_RealConflictResolvesThroughPostAnswer(string winner)
    {
        var master = TestGitRepo.Create();
        var slave = TestGitRepo.Create();
        try
        {
            Seed(master, slave);

            var app = UiHost.BuildHost(master, 0, openBrowser: false);
            await app.StartAsync();
            try
            {
                using var client = new HttpClient();
                using var noRedirect = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
                var baseUrl = BaseUrl(app);

                using var start = await noRedirect.PostAsync($"{baseUrl}/slave/ui/sync", new FormUrlEncodedContent([]));
                start.StatusCode.ShouldBe(HttpStatusCode.Found);
                start.Headers.Location?.ToString().ShouldBe("/slave/ui/sync");

                var conflictPage = await WaitUntilAsync(
                    () => client.GetStringAsync($"{baseUrl}/slave/ui/conflicts"),
                    page => page.Contains("awaiting your call"),
                    TimeSpan.FromSeconds(15));
                conflictPage.ShouldContain("master edit");
                conflictPage.ShouldContain("slave edit");

                using var answer = await noRedirect.PostAsync(
                    $"{baseUrl}/slave/ui/conflicts/answer",
                    new FormUrlEncodedContent(new Dictionary<string, string> { ["path"] = "shared.txt", ["winner"] = winner }));
                answer.StatusCode.ShouldBe(HttpStatusCode.Found);

                var syncPage = await WaitUntilAsync(
                    () => client.GetStringAsync($"{baseUrl}/slave/ui/sync"),
                    page => page.Contains("sync finished") || page.Contains("failed"),
                    TimeSpan.FromSeconds(15));
                syncPage.ShouldContain("sync finished");
                syncPage.ShouldContain($"shared.txt — {winner} wins");

                var history = await client.GetStringAsync($"{baseUrl}/slave/ui/history");
                history.ShouldContain($"shared.txt ({winner} wins)");
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }
        finally
        {
            TryDelete(master);
            TryDelete(slave);
        }
    }

    private static void Seed(string master, string slave)
    {
        TestGitRepo.Commit(master, "shared.txt", "seed", "seed");
        TestGitRepo.Commit(slave, "shared.txt", "seed", "seed");

        var init = StoreInitializer.Initialize(master, new InitOptions());
        (init is Result<InitOutcome>.Ok).ShouldBeTrue();

        StoreInitializer.TryLoadKey(master, out var key).ShouldBeTrue();
        var container = StorePaths.Container(master);
        var loaded = StoreContainer.Load(container, key!);
        (loaded is Result<StoreState>.Ok).ShouldBeTrue();
        var state = ((Result<StoreState>.Ok)loaded).Value;

        state.Slaves["ui"] = new SlaveConfig
        {
            Name = "ui",
            Root = slave,
            SyncBranch = "feature-sync",
            BaseBranch = "main",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        StoreContainer.Save(container, key!, state);

        TestGitRepo.Commit(master, "shared.txt", "master edit", "master change");
        TestGitRepo.Commit(slave, "shared.txt", "slave edit", "slave change");
    }

    private static string BaseUrl(Microsoft.AspNetCore.Builder.WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var port = addresses.Select(a => new Uri(a)).First(u => u.IsLoopback).Port;
        return $"http://127.0.0.1:{port}";
    }

    private static async Task<string> WaitUntilAsync(Func<Task<string>> fetch, Func<string, bool> done, TimeSpan limit)
    {
        var deadline = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < deadline)
        {
            var value = await fetch();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }

        return await fetch();
    }

    private static void TryDelete(string dir)
    {
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static async Task PostResolveAsync(HttpClient client, string baseUrl, string path, string op)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["path"] = path, ["op"] = op });
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var noRedirect = new HttpClient(handler);
        using var response = await noRedirect.PostAsync($"{baseUrl}/slave/feature-ui/conflicts/resolve", content);

        response.StatusCode.ShouldBe(HttpStatusCode.Found);
        response.Headers.Location?.ToString().ShouldBe("/slave/feature-ui/conflicts");
    }

    private static async Task<(Microsoft.AspNetCore.Builder.WebApplication App, int Port)> StartHostAsync()
    {
        var app = UiHost.BuildHost(Directory.GetCurrentDirectory(), 0, openBrowser: false, new FixtureUiStore());
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var port = addresses.Select(a => new Uri(a)).First(u => u.IsLoopback).Port;

        return (app, port);
    }
}
