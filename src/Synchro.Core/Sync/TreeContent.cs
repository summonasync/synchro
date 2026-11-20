using Synchro.Core;
using Synchro.Core.Git;
using Synchro.Core.Hashing;

namespace Synchro.Core.Sync;

public static class TreeContent
{
    public static Result<IReadOnlyList<string>> TrackedFiles(string repo, string sha)
    {
        var result = GitRunner.Run(repo, "ls-tree", "-r", "-z", "--name-only", sha);
        if (!result.Ok)
        {
            return new Result<IReadOnlyList<string>>.Fail(
                SyncError.Invalid($"git ls-tree failed for {sha[..Math.Min(12, sha.Length)]}: {result.StdErr.Trim()}"));
        }

        return new Result<IReadOnlyList<string>>.Ok(
            [.. result.StdOut.Split('\0', StringSplitOptions.RemoveEmptyEntries)]);
    }

    public static Result<IReadOnlyList<FileDelta>> CompareTrees(
        string masterRoot, string masterSha, string slaveRoot, string slaveSha) =>
        Compare(masterRoot, masterSha, slaveRoot, slaveSha, paths: null, EmptySet(), EmptySet(), related: false);

    public static Result<IReadOnlyList<FileDelta>> RebuildTrees(
        string masterRoot, string masterSha, string slaveRoot, string slaveSha,
        IReadOnlySet<string> touchedOnMaster, IReadOnlySet<string> touchedOnSlave) =>
        Compare(masterRoot, masterSha, slaveRoot, slaveSha, paths: null, touchedOnMaster, touchedOnSlave, related: true);

    public static Result<IReadOnlyList<FileDelta>> CompareTouched(
        string masterRoot, string masterSha, string slaveRoot, string slaveSha,
        IReadOnlyList<string> paths, IReadOnlySet<string> touchedOnMaster, IReadOnlySet<string> touchedOnSlave) =>
        Compare(masterRoot, masterSha, slaveRoot, slaveSha, paths, touchedOnMaster, touchedOnSlave, related: true);

    private static Result<IReadOnlyList<FileDelta>> Compare(
        string masterRoot, string masterSha, string slaveRoot, string slaveSha,
        IReadOnlyList<string>? paths, IReadOnlySet<string> touchedOnMaster, IReadOnlySet<string> touchedOnSlave,
        bool related)
    {
        var masterFiles = TrackedFiles(masterRoot, masterSha);
        if (masterFiles is not Result<IReadOnlyList<string>>.Ok masterOk)
        {
            return new Result<IReadOnlyList<FileDelta>>.Fail(((Result<IReadOnlyList<string>>.Fail)masterFiles).Error);
        }

        var slaveFiles = TrackedFiles(slaveRoot, slaveSha);
        if (slaveFiles is not Result<IReadOnlyList<string>>.Ok slaveOk)
        {
            return new Result<IReadOnlyList<FileDelta>>.Fail(((Result<IReadOnlyList<string>>.Fail)slaveFiles).Error);
        }

        var masterSet = masterOk.Value.ToHashSet(StringComparer.Ordinal);
        var slaveSet = slaveOk.Value.ToHashSet(StringComparer.Ordinal);
        var scope = paths ?? [.. masterSet.Union(slaveSet).OrderBy(p => p, StringComparer.Ordinal)];

        var deltas = new List<FileDelta>();
        foreach (var path in scope)
        {
            var masterHash = HashOf(masterRoot, masterSha, path, masterSet);
            if (masterHash is not Result<string?>.Ok masterHashOk)
            {
                return new Result<IReadOnlyList<FileDelta>>.Fail(((Result<string?>.Fail)masterHash).Error);
            }

            var slaveHash = HashOf(slaveRoot, slaveSha, path, slaveSet);
            if (slaveHash is not Result<string?>.Ok slaveHashOk)
            {
                return new Result<IReadOnlyList<FileDelta>>.Fail(((Result<string?>.Fail)slaveHash).Error);
            }

            if (masterHashOk.Value == slaveHashOk.Value)
            {
                continue;
            }

            var stakeMaster = related ? touchedOnMaster.Contains(path) : masterHashOk.Value is not null;
            var stakeSlave = related ? touchedOnSlave.Contains(path) : slaveHashOk.Value is not null;
            FileDeltaKind? kind = (stakeMaster, stakeSlave) switch
            {
                (true, true) => FileDeltaKind.Conflict,
                (true, false) => FileDeltaKind.MasterToSlave,
                (false, true) => FileDeltaKind.SlaveToMaster,
                _ => null
            };
            if (kind is null)
            {
                continue;
            }

            deltas.Add(new FileDelta
            {
                Path = path,
                Kind = kind.Value,
                MasterHash = masterHashOk.Value,
                SlaveHash = slaveHashOk.Value
            });
        }

        return new Result<IReadOnlyList<FileDelta>>.Ok(deltas);
    }

    private static IReadOnlySet<string> EmptySet() => new HashSet<string>(StringComparer.Ordinal);

    private static Result<string?> HashOf(string repo, string sha, string path, HashSet<string> presentFiles)
    {
        if (!presentFiles.Contains(path))
        {
            return new Result<string?>.Ok(null);
        }

        var content = GitChangeService.GetFileContent(repo, path, sha);
        if (content is not Result<byte[]>.Ok contentOk)
        {
            return new Result<string?>.Fail(((Result<byte[]>.Fail)content).Error);
        }

        return new Result<string?>.Ok(ContentHash.Compute(contentOk.Value));
    }
}
