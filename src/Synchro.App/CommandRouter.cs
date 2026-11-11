using Synchro.App.Commands;
using Synchro.App.Mcp;
using Synchro.App.Ui;

namespace Synchro.App;

internal static class CommandRouter
{
    private static readonly string[] KnownCommands = ["init", "pack", "apply", "scene", "status", "log", "serve"];

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return WriteUsage();
        }

        var command = args[0];

        if (command is "--version" or "-v")
        {
            Console.WriteLine($"synchro {AppVersion}");
            return 0;
        }

        if (command is "help" or "--help" or "-h")
        {
            return WriteUsage();
        }

        return command switch
        {
            "init" => InitCommand.Run(args[1..]),
            "pack" => PackCommand.Run(args[1..]),
            "apply" => ApplyCommand.Run(args[1..]),
            "scene" => SceneCommand.Run(args[1..]),
            "ui" => UiHost.Run(args[1..]),
            "serve" => ServerHost.Run(args[1..]),
            "status" => Stub.NotImplemented("status"),
            "log" => Stub.NotImplemented("log"),
            _ => WriteUnknown(command)
        };
    }

    private static int WriteUsage()
    {
        Console.WriteLine("usage: synchro <command> [options]");
        Console.WriteLine($"commands: {string.Join(", ", KnownCommands)}");
        Console.WriteLine("init [options]        set up the local store (--key <hex> to adopt a peer key)");
        Console.WriteLine("pack [--full] [--out] build a package manifest draft from committed state");
        Console.WriteLine("apply <package>       apply a sealed package (stages conflicts into a scene)");
        Console.WriteLine("scene [resolve|commit] work the pending conflict scene");
        Console.WriteLine("help                  show this help");
        Console.WriteLine("run 'synchro <command> --help' for command details");
        return 0;
    }

    private static int WriteUnknown(string command)
    {
        Console.Error.WriteLine($"unknown command '{command}'");
        Console.Error.WriteLine($"commands: {string.Join(", ", KnownCommands)}");
        return 2;
    }

    private static string AppVersion =>
        typeof(CommandRouter).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
