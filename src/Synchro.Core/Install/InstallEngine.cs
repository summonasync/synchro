namespace Synchro.Core.Install;

public sealed class InstallEngine(IInstallPlatform platform)
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public Result<InstallReport> Install(InstallRequest request)
    {
        if (VersionError(request.Version) is { } versionError)
        {
            return new Result<InstallReport>.Fail(versionError);
        }

        if (!File.Exists(request.SourcePath))
        {
            return new Result<InstallReport>.Fail(SyncError.NotFound($"source binary not found: {request.SourcePath}"));
        }

        var notes = new List<string>();
        string? previous;
        var manifest = InstallManifest.Read(platform.Root);
        if (manifest is Result<string?>.Ok manifestOk)
        {
            previous = manifestOk.Value;
        }
        else
        {
            previous = null;
            notes.Add("existing install manifest is unreadable — treating this as a fresh install");
        }

        var target = platform.BinaryPathFor(request.Version);
        if (previous == request.Version && File.Exists(target) && SameFile(request.SourcePath, target))
        {
            return new Result<InstallReport>.Ok(new InstallReport
            {
                UpToDate = true,
                PreviousVersion = previous,
                Version = request.Version,
                BinaryPath = target,
                NameRegistered = true,
                PathReady = true,
                UsedLockedTargetFallback = false,
                Notes = ["already installed at this version — nothing to do"]
            });
        }

        var placement = platform.PlaceBinary(request.SourcePath, request.Version);
        if (placement is not Result<BinaryPlacement>.Ok placed)
        {
            return new Result<InstallReport>.Fail(((Result<BinaryPlacement>.Fail)placement).Error);
        }

        var registration = platform.RegisterName(request.Version, request.ApplyPathFix, request.ForceName);
        if (registration is not Result<NameRegistration>.Ok registered)
        {
            return new Result<InstallReport>.Fail(((Result<NameRegistration>.Fail)registration).Error);
        }

        var prune = platform.RemoveOtherVersions(request.Version);
        if (prune is Result<IReadOnlyList<string>>.Ok pruned && pruned.Value.Count > 0)
        {
            notes.Add($"removed previous version folders: {string.Join(", ", pruned.Value)}");
        }

        var write = InstallManifest.Write(platform.Root, request.Version);
        if (write is not Result<string>.Ok)
        {
            return new Result<InstallReport>.Fail(((Result<string>.Fail)write).Error);
        }

        notes.InsertRange(0, placed.Value.Notes);
        notes.AddRange(registered.Value.Notes);

        return new Result<InstallReport>.Ok(new InstallReport
        {
            UpToDate = false,
            PreviousVersion = previous,
            Version = request.Version,
            BinaryPath = placed.Value.TargetPath,
            NameRegistered = registered.Value.Registered,
            PathReady = registered.Value.PathReady,
            UsedLockedTargetFallback = placed.Value.UsedLockedTargetFallback,
            Notes = notes
        });
    }

    public Result<UninstallReport> Uninstall()
    {
        var notes = new List<string>();
        string? previous;
        var manifest = InstallManifest.Read(platform.Root);
        if (manifest is Result<string?>.Ok manifestOk)
        {
            previous = manifestOk.Value;
        }
        else
        {
            previous = null;
            notes.Add("install manifest is unreadable — removing whatever the layout owns");
        }

        var outcome = platform.Uninstall();
        if (outcome is not Result<UninstallOutcome>.Ok uninstalled)
        {
            return new Result<UninstallReport>.Fail(((Result<UninstallOutcome>.Fail)outcome).Error);
        }

        notes.InsertRange(0, uninstalled.Value.Notes);
        return new Result<UninstallReport>.Ok(new UninstallReport
        {
            NothingInstalled = uninstalled.Value.NothingInstalled,
            RemovedVersion = previous,
            BinaryRemoved = uninstalled.Value.BinaryRemoved,
            NameRemoved = uninstalled.Value.NameRemoved,
            PathEntryRemoved = uninstalled.Value.PathEntryRemoved,
            Notes = notes
        });
    }

    private static SyncError? VersionError(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return SyncError.Invalid("version label is required");
        }

        if (version is "." or ".." ||
            version.IndexOfAny(['/', '\\', Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return SyncError.Invalid($"'{version}' is not a plain version label");
        }

        return null;
    }

    private static bool SameFile(string left, string right) =>
        PathComparer.Equals(
            CanonicalPath.Resolve(left, Path.GetDirectoryName(left)!),
            CanonicalPath.Resolve(right, Path.GetDirectoryName(right)!));
}
