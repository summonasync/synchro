using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synchro.App.Mcp;
using Synchro.Core.Store;

namespace Synchro.App.Ui;

internal static class UiHost
{
    private const string FriendlyHost = "synchro.localhost";

    public static int Run(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        int? portArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out var parsed) || parsed is < 0 or > 65535)
                {
                    Console.Error.WriteLine($"--port must be a number between 0 and 65535, 0 picks a free port (got '{args[i]}')");
                    return 2;
                }

                portArg = parsed;
            }
            else if (args[i] == "--root" && i + 1 < args.Length)
            {
                root = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"unknown ui option '{args[i]}' — use --port <n> or --root <dir>");
                return 2;
            }
        }

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"target directory does not exist: '{root}'");
            return 1;
        }

        root = Path.GetFullPath(root);

        var app = BuildHost(root, portArg ?? 0, openBrowser: true);
        app.Run();
        return 0;
    }

    internal static WebApplication BuildHost(string root, int port, bool openBrowser, IUiStore? store = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        store ??= new StoreUiStore(root);
        var sync = new UiSyncRunner(root);
        var tools = new SynchroMcpTools(root);
        var app = builder.Build();

        app.Use((ctx, next) =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self' 'unsafe-inline'";
            return next();
        });

        app.MapGet("/", () => Html(UiPages.Dashboard(store)));
        app.MapGet("/pack", () => Html(UiPage.Html));

        app.MapGet("/slave/{name}/history", (string name) =>
        {
            if (!store.GetSlaves().Any(s => s.Name == name))
            {
                return UnknownSlave(name);
            }

            return Html(UiPages.History(name, store.GetHistory(name)));
        });

        app.MapGet("/slave/{name}/inbox", (string name) =>
        {
            var slave = store.GetSlaves().FirstOrDefault(s => s.Name == name);
            return slave is null ? UnknownSlave(name) : Html(UiPages.Inbox(slave));
        });

        app.MapGet("/slave/{name}/conflicts", (string name) =>
        {
            if (!store.GetSlaves().Any(s => s.Name == name))
            {
                return UnknownSlave(name);
            }

            return Html(UiPages.Conflicts(name, store.GetConflicts(name), sync));
        });

        app.MapGet("/slave/{name}/sync", (string name) =>
        {
            if (!store.GetSlaves().Any(s => s.Name == name))
            {
                return UnknownSlave(name);
            }

            return Html(UiPages.SyncLive(name, sync));
        });

        app.MapPost("/slave/{name}/sync", (string name) =>
        {
            if (!store.GetSlaves().Any(s => s.Name == name))
            {
                return UnknownSlave(name);
            }

            sync.Start(name);
            return Results.Redirect($"/slave/{Uri.EscapeDataString(name)}/sync");
        });

        app.MapPost("/slave/{name}/conflicts/answer", async (string name, HttpRequest request, CancellationToken aborted) =>
        {
            var form = await request.ReadFormAsync(aborted);
            var winner = form["winner"].ToString() == "slave" ? ConflictWinner.Slave : ConflictWinner.Master;
            sync.TryAnswer(form["path"].ToString(), winner);
            return Results.Redirect($"/slave/{Uri.EscapeDataString(name)}/conflicts");
        });

        app.MapPost("/slave/{name}/conflicts/resolve", async (string name, HttpRequest request, CancellationToken aborted) =>
        {
            var form = await request.ReadFormAsync(aborted);
            var path = form["path"].ToString();
            if (path.Length > 0)
            {
                store.Resolve(name, path, form["op"].ToString());
            }

            return Results.Redirect($"/slave/{Uri.EscapeDataString(name)}/conflicts");
        });

        app.MapGet("/api/status", () => Json(tools.Status()));
        app.MapGet("/api/log", () => Json(tools.Log()));
        app.MapGet("/api/scene", () => Json(tools.SceneStatus()));
        app.MapGet("/api/scene/read", (string path) => Json(tools.SceneRead(path)));

        app.Lifetime.ApplicationStarted.Register(() => Announce(app, root, openBrowser));

        return app;
    }

    private static IResult Html(string markup) => Results.Text(markup, "text/html; charset=utf-8");

    private static IResult Json(string json) => Results.Text(json, "application/json; charset=utf-8");

    private static IResult UnknownSlave(string name) =>
        Results.Text($"unknown slave '{name}'", "text/plain; charset=utf-8", statusCode: 404);

    private static void Announce(WebApplication app, string root, bool openBrowser)
    {
        var port = BoundPort(app);
        var display = $"http://{DisplayHost(port)}";
        Console.WriteLine($"synchro ui: serving {root} at {display} (Ctrl+C to stop)");
        if (openBrowser)
        {
            TryOpenBrowser(display);
        }
    }

    private static int BoundPort(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
        foreach (var address in addresses ?? [])
        {
            if (Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.IsLoopback)
            {
                return uri.Port;
            }
        }

        return 0;
    }

    private static string DisplayHost(int port)
    {
        try
        {
            var probe = Dns.GetHostAddressesAsync(FriendlyHost);
            if (probe.Wait(250) && probe.Result.Any(IPAddress.IsLoopback))
            {
                return $"{FriendlyHost}:{port}";
            }
        }
        catch (Exception)
        {
        }

        return $"127.0.0.1:{port}";
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", url) { UseShellExecute = true };
            Process.Start(psi);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine("browser auto-open unavailable — open the URL manually");
        }
    }
}
