using Synchro.Core;

namespace Synchro.App.Commands;

internal sealed record UninstallArgs
{
    public required bool WantsHelp { get; init; }

    public const string Usage =
        "usage: synchro uninstall — remove the installed synchro binary, its command name and its PATH entries";

    public static Result<UninstallArgs> Parse(IReadOnlyList<string> args)
    {
        var wantsHelp = false;

        foreach (var arg in args)
        {
            if (arg is "--help" or "-h")
            {
                wantsHelp = true;
            }
            else
            {
                return new Result<UninstallArgs>.Fail(SyncError.Invalid(
                    arg.StartsWith('-')
                        ? $"unknown option '{arg}' — {Usage}"
                        : $"unexpected argument '{arg}' — {Usage}"));
            }
        }

        return new Result<UninstallArgs>.Ok(new UninstallArgs { WantsHelp = wantsHelp });
    }
}
