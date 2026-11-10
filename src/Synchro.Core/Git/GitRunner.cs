using System.ComponentModel;
using System.Diagnostics;

namespace Synchro.Core.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public sealed class GitCommandException(string message) : Exception(message);

public static class GitRunner
{
    private const int TimeoutMs = 30_000;

    public static GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = BuildStartInfo(workingDirectory, args);
        using var process = Start(psi);
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

    public static byte[] RunBytes(string workingDirectory, params string[] args)
    {
        var psi = BuildStartInfo(workingDirectory, args);
        using var process = Start(psi);
        var stderr = process.StandardError.ReadToEndAsync();
        using var output = new MemoryStream();
        var copy = process.StandardOutput.BaseStream.CopyToAsync(output);

        if (!process.WaitForExit(TimeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException($"git timed out: {string.Join(' ', args)}");
        }

        process.WaitForExit();
        copy.GetAwaiter().GetResult();
        var errorText = stderr.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw new GitCommandException($"git {string.Join(' ', args)} failed: {errorText.Trim()}");
        }

        return output.ToArray();
    }

    private static ProcessStartInfo BuildStartInfo(string workingDirectory, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
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
