namespace Synchro.Core.Install;

public sealed class LinuxInstallPlatform : IInstallPlatform
{
    private readonly string _homeDir;
    private readonly string? _pathEnvironment;

    public string Name => "linux";

    public string Root { get; }

    public LinuxInstallPlatform(string homeDir, string? xdgDataHome, string? pathEnvironment)
    {
        _homeDir = homeDir;
        _pathEnvironment = pathEnvironment;
        Root = LinuxInstallLayout.DataRoot(homeDir, xdgDataHome);
    }

    public string BinaryPathFor(string version) => LinuxInstallLayout.BinaryPath(Root, version);

    public Result<BinaryPlacement> PlaceBinary(string sourcePath, string version)
    {
        var target = BinaryPathFor(version);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<BinaryPlacement>.Fail(SyncError.Conflict($"install folder could not be created at '{Root}': {ex.Message}"));
        }

        var replace = InstalledBinaryReplacer.Replace(sourcePath, target);
        if (replace is not Result<ReplaceOutcome>.Ok replaced)
        {
            return new Result<BinaryPlacement>.Fail(((Result<ReplaceOutcome>.Fail)replace).Error);
        }

        MakeExecutable(target);
        var outcome = replaced.Value;
        return new Result<BinaryPlacement>.Ok(new BinaryPlacement
        {
            TargetPath = target,
            UsedLockedTargetFallback = outcome.UsedLockedTargetFallback,
            OldCleanupPending = outcome.OldCleanupPending,
            Notes = outcome.OldCleanupPending ? ["previous binary is still locked — it is removed on the next start"] : []
        });
    }

    public Result<NameRegistration> RegisterName(string version, bool pathFixRequested, bool forceName)
    {
        var swap = LinuxNameLink.Swap(LinuxInstallLayout.LinkPath(_homeDir), BinaryPathFor(version), Root, forceName);
        if (swap is not Result<LinkSwapped>.Ok swapped)
        {
            return new Result<NameRegistration>.Fail(((Result<LinkSwapped>.Fail)swap).Error);
        }

        var binDir = LinuxInstallLayout.BinDir(_homeDir);
        if (LinuxPathProbe.Contains(_pathEnvironment, binDir))
        {
            return new Result<NameRegistration>.Ok(new NameRegistration { Registered = true, PathReady = true });
        }

        if (!pathFixRequested)
        {
            return new Result<NameRegistration>.Ok(new NameRegistration
            {
                Registered = true,
                PathReady = false,
                Notes = [$"'{binDir}' is not on PATH — rerun with --path-fix or add it to your PATH manually"]
            });
        }

        var fix = LinuxPathFix.Append(LinuxInstallLayout.ProfilePath(_homeDir), binDir);
        if (fix is not Result<bool>.Ok fixedOk)
        {
            return new Result<NameRegistration>.Fail(((Result<bool>.Fail)fix).Error);
        }

        return new Result<NameRegistration>.Ok(new NameRegistration
        {
            Registered = true,
            PathReady = true,
            Notes = fixedOk.Value ? ["profile block appended — open a new shell to pick up the PATH"] : []
        });
    }

    public Result<IReadOnlyList<string>> RemoveOtherVersions(string keepVersion)
    {
        if (!Directory.Exists(Root))
        {
            return new Result<IReadOnlyList<string>>.Ok([]);
        }

        var removed = new List<string>();
        try
        {
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var name = Path.GetFileName(dir);
                if (name == keepVersion)
                {
                    continue;
                }

                Directory.Delete(dir, true);
                removed.Add(name);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<IReadOnlyList<string>>.Fail(SyncError.Conflict($"old version folders could not be pruned under '{Root}': {ex.Message}"));
        }

        return new Result<IReadOnlyList<string>>.Ok(removed);
    }

    public Result<UninstallOutcome> Uninstall()
    {
        var linkPath = LinuxInstallLayout.LinkPath(_homeDir);
        var linkPresent = File.Exists(linkPath) || LinuxNameLink.IsLink(linkPath);
        if (!Directory.Exists(Root) && !linkPresent)
        {
            return NothingInstalled();
        }

        var notes = new List<string>();
        var nameRemoved = false;
        var binaryRemoved = false;
        try
        {
            if (linkPresent)
            {
                var previous = LinuxNameLink.ResolveTarget(linkPath) ?? linkPath;
                if (LinuxNameLink.OwnedByLayout(previous, Root))
                {
                    File.Delete(linkPath);
                    nameRemoved = true;
                }
                else
                {
                    notes.Add($"left '{linkPath}' in place — it points at '{previous}' outside the synchro layout");
                }
            }

            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
                binaryRemoved = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<UninstallOutcome>.Fail(SyncError.Conflict($"uninstall failed: {ex.Message}"));
        }

        var fix = LinuxPathFix.Remove(LinuxInstallLayout.ProfilePath(_homeDir));
        if (fix is not Result<bool>.Ok fixedOk)
        {
            return new Result<UninstallOutcome>.Fail(((Result<bool>.Fail)fix).Error);
        }

        return new Result<UninstallOutcome>.Ok(new UninstallOutcome
        {
            NothingInstalled = false,
            BinaryRemoved = binaryRemoved,
            NameRemoved = nameRemoved,
            PathEntryRemoved = fixedOk.Value,
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

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
    }
}
