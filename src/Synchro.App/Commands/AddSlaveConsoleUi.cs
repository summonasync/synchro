using Synchro.Core.Slave;

namespace Synchro.App.Commands;

internal sealed class AddSlaveConsoleUi : IAddSlaveUi
{
    public string AskSlaveName()
    {
        Console.Write("slave name: ");
        return Read();
    }

    public string AskSyncBranch(string suggestion)
    {
        Console.Write($"sync branch [{suggestion}]: ");
        return Read();
    }

    public string AskBaseBranch(string? suggestion)
    {
        Console.Write(suggestion is null ? "base branch: " : $"base branch [{suggestion}]: ");
        return Read();
    }

    public bool ConfirmBootstrap(string root)
    {
        Console.WriteLine($"'{root}' has no git repository.");
        Console.WriteLine("with consent synchro will run git init there and make ONE bootstrap commit,");
        Console.WriteLine("with neutral identity (user.name=synchro) and message 'synchro bootstrap'.");
        Console.Write("proceed? [y/N]: ");
        return Read().ToLowerInvariant() is "y" or "yes";
    }

    private static string Read() => Console.ReadLine()?.Trim() ?? string.Empty;
}
