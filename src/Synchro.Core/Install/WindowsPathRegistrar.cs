namespace Synchro.Core.Install;

public static class WindowsPathRegistrar
{
    public const char Separator = ';';

    public static Result<PathRegistryChange> Add(IUserPathStore store, string entry)
    {
        var read = store.Read();
        if (read is not Result<string?>.Ok readOk)
        {
            return new Result<PathRegistryChange>.Fail(((Result<string?>.Fail)read).Error);
        }

        var current = readOk.Value;
        if (string.IsNullOrEmpty(current))
        {
            return WriteChange(store, new PathRegistryChange(true, false, entry));
        }

        var segments = current.Split(Separator);
        if (segments.Any(segment => Matches(segment, entry)))
        {
            return new Result<PathRegistryChange>.Ok(new PathRegistryChange(false, true, current));
        }

        var updated = current.EndsWith(Separator) ? current + entry : current + Separator + entry;
        return WriteChange(store, new PathRegistryChange(true, false, updated));
    }

    public static Result<PathRegistryChange> Remove(IUserPathStore store, string entry)
    {
        var read = store.Read();
        if (read is not Result<string?>.Ok readOk)
        {
            return new Result<PathRegistryChange>.Fail(((Result<string?>.Fail)read).Error);
        }

        var current = readOk.Value;
        if (current is null)
        {
            return new Result<PathRegistryChange>.Ok(new PathRegistryChange(false, false, string.Empty));
        }

        var segments = current.Split(Separator);
        var kept = segments.Where(segment => !Matches(segment, entry)).ToArray();
        if (kept.Length == segments.Length)
        {
            return new Result<PathRegistryChange>.Ok(new PathRegistryChange(false, false, current));
        }

        return WriteChange(store, new PathRegistryChange(true, false, string.Join(Separator, kept)));
    }

    private static Result<PathRegistryChange> WriteChange(IUserPathStore store, PathRegistryChange change)
    {
        var write = store.Write(change.Value);
        return write is Result<string>.Ok
            ? new Result<PathRegistryChange>.Ok(change)
            : new Result<PathRegistryChange>.Fail(((Result<string>.Fail)write).Error);
    }

    private static bool Matches(string segment, string entry) =>
        segment.Trim().Equals(entry, StringComparison.OrdinalIgnoreCase);
}
