using Synchro.Core.Store;

namespace Synchro.Core.Landing;

public static class InFlightMarker
{
    public const string FolderName = "in-flight";

    public static string PathFor(string storeRoot, string slaveName) =>
        System.IO.Path.Combine(StorePaths.Folder(storeRoot), FolderName, $"{slaveName}.marker");

    public static bool Exists(string markerPath) => File.Exists(markerPath);

    public static void Set(string markerPath)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(markerPath)!);
        File.WriteAllText(markerPath, $"in-flight since {DateTimeOffset.UtcNow:O}");
    }

    public static void Clear(string markerPath)
    {
        if (File.Exists(markerPath))
        {
            File.Delete(markerPath);
        }
    }
}
