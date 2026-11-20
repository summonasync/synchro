using Synchro.Core;

namespace Synchro.App.Commands;

internal sealed record InstallArgs
{
    public required bool PathFix { get; init; }

    public required bool Force { get; init; }

    public required bool WantsHelp { get; init; }

    public const string Usage =
        "usage: synchro install [--path-fix] [--force] — copy the running binary into the per-user install location and register the 'synchro' command";

    public static Result<InstallArgs> Parse(IReadOnlyList<string> args)
    {
        var pathFix = false;
        var force = false;
        var wantsHelp = false;

        foreach (var arg in args)
        {
            if (arg == "--path-fix")
            {
                pathFix = true;
            }
            else if (arg == "--force")
            {
                force = true;
            }
            else if (arg is "--help" or "-h")
            {
                wantsHelp = true;
            }
            else
            {
                return new Result<InstallArgs>.Fail(SyncError.Invalid(
                    arg.StartsWith('-')
                        ? $"unknown option '{arg}' — {Usage}"
                        : $"unexpected argument '{arg}' — {Usage}"));
            }
        }

        return new Result<InstallArgs>.Ok(new InstallArgs { PathFix = pathFix, Force = force, WantsHelp = wantsHelp });
    }
}
