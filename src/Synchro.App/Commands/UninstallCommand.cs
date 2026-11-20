using Synchro.Core;
using Synchro.Core.Install;

namespace Synchro.App.Commands;

internal static class UninstallCommand
{
    public static int Run(string[] args)
    {
        var parsed = UninstallArgs.Parse(args);
        if (parsed is not Result<UninstallArgs>.Ok parsedOk)
        {
            Console.Error.WriteLine(((Result<UninstallArgs>.Fail)parsed).Error.Message);
            return 2;
        }

        if (parsedOk.Value.WantsHelp)
        {
            Console.WriteLine(UninstallArgs.Usage);
            return 0;
        }

        var platform = InstallPlatform.Select();
        if (platform is not Result<IInstallPlatform>.Ok platformOk)
        {
            Console.Error.WriteLine($"uninstall failed: {((Result<IInstallPlatform>.Fail)platform).Error.Message}");
            return 1;
        }

        return Run(parsedOk.Value, platformOk.Value, Console.Out, Console.Error);
    }

    internal static int Run(UninstallArgs args, IInstallPlatform platform, TextWriter output, TextWriter error)
    {
        var report = new InstallEngine(platform).Uninstall();
        if (report is not Result<UninstallReport>.Ok ok)
        {
            error.WriteLine($"uninstall failed: {((Result<UninstallReport>.Fail)report).Error.Message}");
            return 1;
        }

        WriteReport(ok.Value, output);
        return 0;
    }

    private static void WriteReport(UninstallReport report, TextWriter output)
    {
        if (report.NothingInstalled)
        {
            output.WriteLine("nothing installed — nothing to remove");
            return;
        }

        output.WriteLine(report.RemovedVersion is { } version
            ? $"uninstalled: synchro {version}"
            : "uninstalled: synchro");

        if (report.BinaryRemoved)
        {
            output.WriteLine("  binary removed");
        }

        if (report.NameRemoved)
        {
            output.WriteLine("  command name removed");
        }

        if (report.PathEntryRemoved)
        {
            output.WriteLine("  PATH entry removed");
        }

        foreach (var note in report.Notes)
        {
            output.WriteLine($"  {note}");
        }
    }
}
