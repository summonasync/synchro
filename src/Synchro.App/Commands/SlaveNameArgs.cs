using Synchro.Core;

namespace Synchro.App.Commands;

internal sealed record SlaveNameArgs
{
    public required string? SlaveName { get; init; }

    public required bool WantsHelp { get; init; }

    public static Result<SlaveNameArgs> Parse(IReadOnlyList<string> args, string usage)
    {
        var positional = new List<string>();
        var wantsHelp = false;

        foreach (var arg in args)
        {
            if (arg is "--help" or "-h")
            {
                wantsHelp = true;
            }
            else if (arg.StartsWith('-'))
            {
                return new Result<SlaveNameArgs>.Fail(SyncError.Invalid($"unknown option '{arg}' — {usage}"));
            }
            else
            {
                positional.Add(arg);
            }
        }

        if (positional.Count > 1)
        {
            return new Result<SlaveNameArgs>.Fail(SyncError.Invalid($"too many arguments — {usage}"));
        }

        return new Result<SlaveNameArgs>.Ok(new SlaveNameArgs
        {
            SlaveName = positional.FirstOrDefault(),
            WantsHelp = wantsHelp
        });
    }
}
