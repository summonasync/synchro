using System.Diagnostics;

namespace Synchro.Tests;

public static class GitCli
{
    public static (int ExitCode, string Output) Run(string workingDirectory, params string[] arguments)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit(10_000);
        return (process.ExitCode, output);
    }

    public static void Init(string workingDirectory) =>
        Run(workingDirectory, "init", "-b", "main");

    public static string StatusPorcelain(string workingDirectory) =>
        Run(workingDirectory, "status", "--porcelain").Output;
}
