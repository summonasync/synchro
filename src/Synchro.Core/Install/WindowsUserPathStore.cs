using Microsoft.Win32;

namespace Synchro.Core.Install;

public sealed class WindowsUserPathStore : IUserPathStore
{
    private const string EnvironmentKey = "Environment";
    private const string PathValueName = "PATH";

    public Result<string?> Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Result<string?>.Fail(SyncError.Invalid("the user PATH registry store requires windows"));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey);
            var value = key?.GetValue(PathValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
            return new Result<string?>.Ok(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new Result<string?>.Fail(SyncError.Conflict($"user PATH could not be read from the registry: {ex.Message}"));
        }
    }

    public Result<string> Write(string value)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new Result<string>.Fail(SyncError.Invalid("the user PATH registry store requires windows"));
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(EnvironmentKey, writable: true);
            if (key is null)
            {
                return new Result<string>.Fail(SyncError.Conflict("registry Environment key could not be opened or created"));
            }

            RegistryValueKind kind;
            try
            {
                kind = key.GetValueKind(PathValueName);
            }
            catch (Exception ex) when (ex is IOException or System.Security.SecurityException)
            {
                kind = RegistryValueKind.ExpandString;
            }

            key.SetValue(PathValueName, value, kind);
            return new Result<string>.Ok(value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new Result<string>.Fail(SyncError.Conflict($"user PATH could not be written to the registry: {ex.Message}"));
        }
    }
}
