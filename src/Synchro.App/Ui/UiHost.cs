using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Synchro.App.Mcp;

namespace Synchro.App.Ui;

internal static class UiHost
{
    private const int DefaultPort = 7741;

    public static int Run(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        int? portArg = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out var parsed) || parsed is < 1 or > 65535)
                {
                    Console.Error.WriteLine($"--port must be a number between 1 and 65535 (got '{args[i]}')");
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

        var port = portArg ?? DefaultPort;

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"target directory does not exist: '{root}'");
            return 1;
        }

        root = Path.GetFullPath(root);
        var url = $"http://127.0.0.1:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(url);

        var tools = new SynchroMcpTools(root);
        var app = builder.Build();

        app.MapGet("/", (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentSecurityPolicy = "default-src 'self' 'unsafe-inline'";
            return Results.Text(UiPage.Html, "text/html; charset=utf-8");
        });
        app.MapGet("/api/status", () => Json(tools.Status()));
        app.MapGet("/api/log", () => Json(tools.Log()));
        app.MapGet("/api/scene", () => Json(tools.SceneStatus()));
        app.MapGet("/api/scene/read", (string path) => Json(tools.SceneRead(path)));

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Console.WriteLine($"synchro ui: serving {root} at {url} (Ctrl+C to stop)");
            TryOpenBrowser(url);
        });

        app.Run();
        return 0;
    }

    private static IResult Json(string json) => Results.Text(json, "application/json; charset=utf-8");

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
