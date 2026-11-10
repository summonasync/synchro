using Synchro.App.Commands;

namespace Synchro.App;

internal static class CommandRouter
{
    private static readonly string[] KnownCommands = ["init", "pack", "apply", "status", "log", "serve"];

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

        return command switch
        {
            "init" => Stub.NotImplemented("init"),
            "pack" => Stub.NotImplemented("pack"),
            "apply" => Stub.NotImplemented("apply"),
            "status" => Stub.NotImplemented("status"),
            "log" => Stub.NotImplemented("log"),
            "serve" => Stub.NotImplemented("serve"),
            _ => WriteUnknown(command)
        };
    }

    private static int WriteUsage()
    {
        Console.WriteLine("usage: synchro <command> [options]");
        Console.WriteLine($"commands: {string.Join(", ", KnownCommands)}");
        return 2;
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
