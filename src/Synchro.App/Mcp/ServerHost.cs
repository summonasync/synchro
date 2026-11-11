using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synchro.App.Mcp;

namespace Synchro.App.Mcp;

internal static class ServerHost
{
    public static int Run(string[] args)
    {
        var root = Directory.GetCurrentDirectory();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--root" && i + 1 < args.Length)
            {
                root = args[++i];
            }
            else if (args[i].StartsWith("--root="))
            {
                root = args[i]["--root=".Length..];
            }
            else
            {
                Console.Error.WriteLine($"unknown serve option '{args[i]}'");
                return 2;
            }
        }

        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"target directory does not exist: '{root}'");
            return 1;
        }

        root = Path.GetFullPath(root);

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        if (Environment.GetEnvironmentVariable("SYNCHRO_MCP_DEBUG") == "1")
        {
            builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        }
        builder.Services.AddSingleton(new SynchroMcpTools(root));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<SynchroMcpTools>();

        builder.Build().RunAsync().GetAwaiter().GetResult();
        return 0;
    }
}
