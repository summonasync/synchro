using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Synchro.App.Ui;
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
