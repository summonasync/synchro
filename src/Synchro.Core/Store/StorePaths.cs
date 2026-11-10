namespace Synchro.Core.Store;

public static class StorePaths
{
    public const string FolderName = ".synchro";
    public const string KeyFileName = "key";
    public const string ContainerFileName = "container";

    public static string Folder(string root) => Path.Combine(root, FolderName);

    public static string Key(string root) => Path.Combine(root, FolderName, KeyFileName);

    public static string Container(string root) => Path.Combine(root, FolderName, ContainerFileName);
}
