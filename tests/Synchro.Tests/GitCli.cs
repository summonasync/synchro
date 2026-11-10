using System.ComponentModel;
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

        Process process;
        try
        {
            process = Process.Start(psi)!;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("git binary is required for these tests", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(10_000);
        var output = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        return (process.ExitCode, output);
    }

    public static void Init(string workingDirectory) =>
        Run(workingDirectory, "init", "-b", "main");

    public static string StatusPorcelain(string workingDirectory) =>
        Run(workingDirectory, "status", "--porcelain").Output;
}
