using Synchro.Core;
using Synchro.Core.Install;

namespace Synchro.App.Commands;

internal sealed record InstallRuntime
{
    public required string SourcePath { get; init; }

    public required string Version { get; init; }

    public required IInstallPlatform Platform { get; init; }

    public string? NameLinkPath { get; init; }

    internal static Result<InstallRuntime> Discover()
    {
        var selected = InstallPlatform.Select();
        if (selected is not Result<IInstallPlatform>.Ok selectedOk)
        {
            return new Result<InstallRuntime>.Fail(((Result<IInstallPlatform>.Fail)selected).Error);
        }

        var sourcePath = Environment.ProcessPath;
        if (sourcePath is null)
        {
            return new Result<InstallRuntime>.Fail(SyncError.Conflict(
                "the running executable path is unavailable — install from a published binary"));
        }

        return new Result<InstallRuntime>.Ok(new InstallRuntime
        {
            SourcePath = sourcePath,
            Version = typeof(InstallRuntime).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            Platform = selectedOk.Value,
            NameLinkPath = NameLinkPathFor(selectedOk.Value)
        });
    }

    private static string? NameLinkPathFor(IInstallPlatform platform) =>
        platform is LinuxInstallPlatform
            ? LinuxInstallLayout.LinkPath(
                Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            : null;
}
