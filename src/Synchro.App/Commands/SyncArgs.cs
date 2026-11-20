using Synchro.Core;

namespace Synchro.App.Commands;

internal sealed record SyncArgs
{
    public required string? SlaveName { get; init; }

    public required bool AssumeYes { get; init; }

    public required bool WantsHelp { get; init; }

    public const string Usage =
        "usage: synchro sync [<slave>] [--yes] — bidirectional sync; --yes resolves conflicts master-wins without asking";

    public static Result<SyncArgs> Parse(IReadOnlyList<string> args)
    {
        var positional = new List<string>();
        var assumeYes = false;
        var wantsHelp = false;

        foreach (var arg in args)
        {
            if (arg == "--yes")
            {
                assumeYes = true;
            }
            else if (arg is "--help" or "-h")
            {
                wantsHelp = true;
            }
            else if (arg.StartsWith('-'))
            {
                return new Result<SyncArgs>.Fail(SyncError.Invalid($"unknown option '{arg}' — {Usage}"));
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (positional.Count > 1)
        {
            return new Result<SyncArgs>.Fail(SyncError.Invalid($"too many arguments — {Usage}"));
        }

        return new Result<SyncArgs>.Ok(new SyncArgs
        {
            SlaveName = positional.FirstOrDefault(),
            AssumeYes = assumeYes,
            WantsHelp = wantsHelp
        });
    }
}
