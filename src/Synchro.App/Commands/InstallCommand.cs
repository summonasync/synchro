using Synchro.Core;
using Synchro.Core.Install;

namespace Synchro.App.Commands;

internal static class InstallCommand
{
    public static int Run(string[] args)
    {
        var parsed = InstallArgs.Parse(args);
        if (parsed is not Result<InstallArgs>.Ok parsedOk)
        {
            Console.Error.WriteLine(((Result<InstallArgs>.Fail)parsed).Error.Message);
            return 2;
        }

        if (parsedOk.Value.WantsHelp)
        {
            Console.WriteLine(InstallArgs.Usage);
            return 0;
        }

        var runtime = InstallRuntime.Discover();
        if (runtime is not Result<InstallRuntime>.Ok runtimeOk)
        {
            Console.Error.WriteLine($"install failed: {((Result<InstallRuntime>.Fail)runtime).Error.Message}");
            return 1;
        }

        return Run(parsedOk.Value, runtimeOk.Value, Console.Out, Console.Error);
    }

    internal static int Run(InstallArgs args, InstallRuntime runtime, TextWriter output, TextWriter error)
    {
        WarnForeignNameLink(args, runtime, output);

        var report = new InstallEngine(runtime.Platform).Install(new InstallRequest
        {
            SourcePath = runtime.SourcePath,
            Version = runtime.Version,
            ApplyPathFix = args.PathFix,
            ForceName = args.Force
        });

        if (report is not Result<InstallReport>.Ok ok)
        {
            error.WriteLine($"install failed: {((Result<InstallReport>.Fail)report).Error.Message}");
            return 1;
        }

        WriteReport(ok.Value, output);
        return 0;
    }

    private static void WarnForeignNameLink(InstallArgs args, InstallRuntime runtime, TextWriter output)
    {
        if (!args.Force || runtime.NameLinkPath is not { } link)
        {
            return;
        }

        if (!File.Exists(link) && !LinuxNameLink.IsLink(link))
        {
            return;
        }

        var previous = LinuxNameLink.ResolveTarget(link) ?? link;
        if (!LinuxNameLink.OwnedByLayout(previous, runtime.Platform.Root))
        {
            output.WriteLine(
                $"warning: re-pointing '{link}' — it currently points at '{previous}' outside the synchro layout");
        }
    }

    private static void WriteReport(InstallReport report, TextWriter output)
    {
        if (report.UpToDate)
        {
            output.WriteLine($"synchro {report.Version} is already installed — nothing to do");
            output.WriteLine($"  {report.BinaryPath}");
            return;
        }

        output.WriteLine($"installed: synchro {report.Version} -> {report.BinaryPath}");
        if (report.PreviousVersion is { } previous && previous != report.Version)
        {
            output.WriteLine($"  upgrade: {previous} -> {report.Version}");
        }

        if (report.UsedLockedTargetFallback)
        {
            output.WriteLine("  previous binary was locked — replaced through the old/new swap");
        }

        foreach (var note in report.Notes)
        {
            output.WriteLine($"  {note}");
        }
    }
}
