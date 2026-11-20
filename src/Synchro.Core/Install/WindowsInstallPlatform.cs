namespace Synchro.Core.Install;

public sealed class WindowsInstallPlatform : IInstallPlatform
{
    private readonly IUserPathStore _pathStore;

    public string Name => "windows";

    public string Root { get; }

    public WindowsInstallPlatform(string localAppData, IUserPathStore pathStore)
    {
        Root = WindowsInstallLayout.ProgramDir(localAppData);
        _pathStore = pathStore;
    }

    public string BinaryPathFor(string version) => WindowsInstallLayout.ExecutableIn(Root);

    public Result<BinaryPlacement> PlaceBinary(string sourcePath, string version)
    {
        try
        {
            Directory.CreateDirectory(Root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<BinaryPlacement>.Fail(SyncError.Conflict($"install folder could not be created at '{Root}': {ex.Message}"));
        }

        var executable = WindowsInstallLayout.ExecutableIn(Root);
        var replace = InstalledBinaryReplacer.Replace(sourcePath, executable);
        if (replace is not Result<ReplaceOutcome>.Ok replaced)
        {
            return new Result<BinaryPlacement>.Fail(((Result<ReplaceOutcome>.Fail)replace).Error);
        }

        var outcome = replaced.Value;
        return new Result<BinaryPlacement>.Ok(new BinaryPlacement
        {
            TargetPath = executable,
            UsedLockedTargetFallback = outcome.UsedLockedTargetFallback,
            OldCleanupPending = outcome.OldCleanupPending,
            Notes = outcome.OldCleanupPending ? ["previous executable is still locked — it is removed on the next start"] : []
        });
    }

    public Result<NameRegistration> RegisterName(string version, bool pathFixRequested, bool forceName)
    {
        var notes = new List<string>();
        if (pathFixRequested)
        {
            notes.Add("--path-fix applies to linux profiles; the windows PATH is registered in the registry");
        }

        var add = WindowsPathRegistrar.Add(_pathStore, Root);
        if (add is not Result<PathRegistryChange>.Ok added)
        {
            return new Result<NameRegistration>.Fail(((Result<PathRegistryChange>.Fail)add).Error);
        }

        if (added.Value.Changed)
        {
            WindowsEnvironmentBroadcast.NotifyEnvironmentChanged();
            notes.Add("PATH updated — open a new terminal to pick it up");
        }

        return new Result<NameRegistration>.Ok(new NameRegistration { Registered = true, PathReady = true, Notes = notes });
    }

    public Result<IReadOnlyList<string>> RemoveOtherVersions(string keepVersion) =>
        new Result<IReadOnlyList<string>>.Ok([]);

    public Result<UninstallOutcome> Uninstall()
    {
        if (!Directory.Exists(Root))
        {
            return NothingInstalled();
        }

        var notes = new List<string>();
        var remove = WindowsPathRegistrar.Remove(_pathStore, Root);
        if (remove is not Result<PathRegistryChange>.Ok removedOk)
        {
            return new Result<UninstallOutcome>.Fail(((Result<PathRegistryChange>.Fail)remove).Error);
        }

        if (removedOk.Value.Changed)
        {
            WindowsEnvironmentBroadcast.NotifyEnvironmentChanged();
            notes.Add("PATH entry removed — already open terminals keep resolving synchro until restarted");
        }

        var binaryRemoved = false;
        try
        {
            var executable = WindowsInstallLayout.ExecutableIn(Root);
            if (File.Exists(executable))
            {
                File.Delete(executable);
                binaryRemoved = true;
            }

            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"installation folder could not be fully removed — {ex.Message}; leftovers are cleaned up on the next install");
        }

        return new Result<UninstallOutcome>.Ok(new UninstallOutcome
        {
            NothingInstalled = false,
            BinaryRemoved = binaryRemoved,
            NameRemoved = removedOk.Value.Changed,
            PathEntryRemoved = removedOk.Value.Changed,
            Notes = notes
        });
    }

    private static Result<UninstallOutcome> NothingInstalled() =>
        new Result<UninstallOutcome>.Ok(new UninstallOutcome
        {
            NothingInstalled = true,
            BinaryRemoved = false,
            NameRemoved = false,
            PathEntryRemoved = false
        });
}
