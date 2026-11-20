using Synchro.Core;
using Synchro.Core.Package;

namespace Synchro.Core.Landing;

public static class WorkingTreeWriter
{
    public static Result<LandingEdits> Apply(string receivingRoot, IReadOnlyList<LandingChange> changes)
    {
        if (changes.Count == 0)
        {
            return new Result<LandingEdits>.Ok(LandingEdits.Empty);
        }

        var paths = changes.SelectMany(TouchedPaths);

        var pathError = PackagePaths.Validate(receivingRoot, paths);
        if (pathError is not null)
        {
            return new Result<LandingEdits>.Fail(pathError);
        }

        var written = new List<string>();
        var deleted = new List<string>();

        try
        {
            foreach (var change in changes)
            {
                switch (change)
                {
                    case LandingChange.Write write:
                        WriteContent(receivingRoot, write.Path, write.Content);
                        written.Add(write.Path);
                        break;
                    case LandingChange.Rename rename:
                        WriteContent(receivingRoot, rename.To, rename.Content);
                        written.Add(rename.To);
                        if (DeleteQuietly(receivingRoot, rename.From))
                        {
                            deleted.Add(rename.From);
                        }

                        break;
                    case LandingChange.Delete delete:
                        if (DeleteQuietly(receivingRoot, delete.Path))
                        {
                            deleted.Add(delete.Path);
                        }

                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Result<LandingEdits>.Fail(SyncError.Invalid(
                $"landing failed midway ({ex.Message}) — fix the blocker and run sync again; already-written files are recognised as landed"));
        }

        return new Result<LandingEdits>.Ok(new LandingEdits(written, deleted));
    }

    private static IEnumerable<string> TouchedPaths(LandingChange change) => change switch
    {
        LandingChange.Write write => [write.Path],
        LandingChange.Rename rename => [rename.From, rename.To],
        LandingChange.Delete delete => [delete.Path],
        _ => []
    };

    private static void WriteContent(string root, string path, byte[] content)
    {
        var target = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, content);
    }

    private static bool DeleteQuietly(string root, string path)
    {
        var target = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target))
        {
            return false;
        }

        File.Delete(target);
        return true;
    }
}
