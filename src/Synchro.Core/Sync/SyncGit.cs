using System.ComponentModel;
using System.Diagnostics;
using Synchro.Core.Git;

namespace Synchro.Core.Sync;

public static class SyncGit
{
    private const int TimeoutMs = 30_000;

    public static GitResult Run(string workingDirectory, string? alternateObjectsDir, params string[] args)
    {
        var psi = BuildStartInfo(workingDirectory, alternateObjectsDir, args);
        using var process = Start(psi);
        process.StandardInput.Close();
        return Collect(process, args);
    }

    public static GitResult RunWithStdin(
        string workingDirectory, string? alternateObjectsDir, string input, params string[] args)
    {
        var psi = BuildStartInfo(workingDirectory, alternateObjectsDir, args);
        using var process = Start(psi);
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        return Collect(process, args);
    }

    private static GitResult Collect(Process process, string[] args)
    {
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(TimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"git timed out: {string.Join(' ', args)}");
        }

        process.WaitForExit();
        return new GitResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
    }

    private static ProcessStartInfo BuildStartInfo(string workingDirectory, string? alternateObjectsDir, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };
        if (alternateObjectsDir is not null)
        {
            psi.EnvironmentVariables["GIT_ALTERNATE_OBJECT_DIRECTORIES"] = alternateObjectsDir;
        }

        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.quotepath=false");
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        return psi;
    }

    private static Process Start(ProcessStartInfo psi)
    {
        try
        {
            return Process.Start(psi)!;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("git binary is required for synchro git-mode", ex);
        }
    }
}
