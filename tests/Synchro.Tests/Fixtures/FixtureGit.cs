using System.Diagnostics;

namespace Synchro.Tests.Fixtures;

public sealed record FixtureGitResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

public static class FixtureGit
{
    public static FixtureGitResult Run(string workingDirectory, params string[] arguments) =>
        Execute(workingDirectory, null, null, arguments);

    public static FixtureGitResult RunWithEnv(
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment,
        params string[] arguments) =>
        Execute(workingDirectory, environment, null, arguments);

    public static FixtureGitResult RunWithInput(
        string workingDirectory,
        string standardInput,
        params string[] arguments) =>
        Execute(workingDirectory, null, standardInput, arguments);

    public static string Output(string workingDirectory, params string[] arguments)
    {
        var result = Run(workingDirectory, arguments);
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"git {string.Join(" ", arguments)} failed in {workingDirectory}: {result.StandardError.Trim()}");
        return result.StandardOutput.Trim();
    }

    public static IReadOnlyDictionary<string, string> DateEnv(DateTimeOffset date)
    {
        var pinned = date.ToString("yyyy-MM-dd'T'HH:mm:sszzz");
        return new Dictionary<string, string>
        {
            ["GIT_AUTHOR_DATE"] = pinned,
            ["GIT_COMMITTER_DATE"] = pinned
        };
    }

    private static FixtureGitResult Execute(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        string? standardInput,
        string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = standardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                start.EnvironmentVariables[key] = value;
            }
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }

        process.WaitForExit(10_000);
        return new FixtureGitResult(
            process.ExitCode,
            stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult());
    }
}
