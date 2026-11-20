namespace Synchro.Core.Landing;

public abstract record LandingChange
{
    public sealed record Write(string Path, byte[] Content) : LandingChange;

    public sealed record Rename(string From, string To, byte[] Content) : LandingChange;

    public sealed record Delete(string Path) : LandingChange;

    public static LandingChange.Write WriteFile(string path, string content) =>
        new(path, System.Text.Encoding.UTF8.GetBytes(content));

    public static LandingChange.Rename RenameFile(string from, string to, string content) =>
        new(from, to, System.Text.Encoding.UTF8.GetBytes(content));
}
