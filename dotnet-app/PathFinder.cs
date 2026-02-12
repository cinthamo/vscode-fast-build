namespace FastBuild;

public static class PathFinder
{
    private static string? Loop(string filePath, Func<string, string?> check)
    {
        string? dir = Directory.Exists(filePath) ? filePath
            : Path.GetDirectoryName(filePath) ?? throw new ArgumentNullException(nameof(filePath));
        
        while (dir != null)
        {
            string? result = check(dir);
            if (result != null)
                return result;
            
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }

    public static string? FindFastBuildDirectory(string filePath)
    {
        return Loop(filePath, dir =>
        {
            string fastBuildDirectory = Path.Combine(dir, ".fastbuild");
            return Directory.Exists(fastBuildDirectory) ? fastBuildDirectory : null;
        });
    }
    
    public static string? FindCsprojFileAsync(string filePath)
    {
        bool compatibilityMode = Environment.GetEnvironmentVariable("FASTBUILD_COMPATIBILITY_MODE") == "true";
        if (compatibilityMode)
        {
            // Old logic: always start from parent directory
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
        else
        {
            return Loop(filePath, dir =>
            {
                string[] files = Directory.GetFiles(dir, "*.csproj");
                string? csproj = Array.Find(files, file => file.EndsWith(".csproj") && !file.EndsWith(".fastbuild.csproj"));
                return csproj;
            });
        }
    }

    public static string? FindGlobalJsonFileAsync(string filePath)
    {
        return Loop(filePath, dir =>
        {
            string path = Path.Combine(dir, "global.json");
            return File.Exists(path) ? path : null;
        });
    }
}
