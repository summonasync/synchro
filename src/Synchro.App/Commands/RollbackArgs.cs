using Synchro.Core;

namespace Synchro.App.Commands;

internal sealed record RollbackArgs
{
    public required string? SlaveName { get; init; }
    public required bool PreferMaster { get; init; }
    public required bool WantsHelp { get; init; }

    public const string Usage =
        "usage: synchro rollback [<slave>] [--master] — return the receiving repo to its base branch and discard unlanded sync changes";

    public static Result<RollbackArgs> Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        var preferMaster = false;
        var wantsHelp = false;

        foreach (var arg in args)
        {
            if (arg == "--master")
            {
                preferMaster = true;
            }
            else if (arg is "--help" or "-h")
            {
                wantsHelp = true;
            }
            else if (arg.StartsWith('-'))
            {
                return new Result<RollbackArgs>.Fail(SyncError.Invalid($"unknown option '{arg}' — {Usage}"));
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (positional.Count > 1)
        {
            return new Result<RollbackArgs>.Fail(SyncError.Invalid($"too many arguments — {Usage}"));
        }

        return new Result<RollbackArgs>.Ok(new RollbackArgs
        {
            SlaveName = positional.FirstOrDefault(),
            PreferMaster = preferMaster,
            WantsHelp = wantsHelp
        });
    }
}
