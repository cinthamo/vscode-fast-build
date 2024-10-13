namespace FastBuild;

public static class PathFinder
{
    public static string? FindFastBuildDirectory(string directory)
    {
        string? dir = directory;
        while (dir != null)
        {
            string fastBuildDirectory = Path.Combine(dir, ".fastbuild");
            if (Directory.Exists(fastBuildDirectory))
                return fastBuildDirectory;

            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
    
    public static string? FindCsprojFileAsync(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath) ?? throw new ArgumentNullException(nameof(filePath));
        while (dir != null)
        {
            string[] files = Directory.GetFiles(dir, "*.csproj");
            string? csproj = Array.Find(files, file => file.EndsWith(".csproj") && !file.EndsWith(".fastbuild.csproj"));
            if (csproj != null)
                return csproj;

            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    public static string? FindGlobalJsonFileAsync(string filePath)
    {
        string? dir = Path.GetDirectoryName(filePath) ?? throw new ArgumentNullException(nameof(filePath));
        while (dir != null)
        {
            string path = Path.Combine(dir, "global.json");
            if (File.Exists(path))
                return path;

            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
