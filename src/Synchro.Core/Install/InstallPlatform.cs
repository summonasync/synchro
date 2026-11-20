namespace Synchro.Core.Install;

public static class InstallPlatform
{
    public static Result<IInstallPlatform> Select()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Result<IInstallPlatform>.Ok(new WindowsInstallPlatform(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                new WindowsUserPathStore()));
        }

        if (OperatingSystem.IsLinux())
        {
            return new Result<IInstallPlatform>.Ok(new LinuxInstallPlatform(
                Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                Environment.GetEnvironmentVariable("PATH")));
        }

        return new Result<IInstallPlatform>.Fail(SyncError.Invalid("synchro install supports linux and windows only"));
    }
}
