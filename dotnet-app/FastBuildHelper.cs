using System.Text.Json;
using FastBuild;
using static OutputHandler;

public static class FastBuildMain
{
    private class Config
    {
        public string? Requires { get; set; }

        public class CommandConfig
        {
            public List<string> Files { get; set; } = [];
            public string? Command { get; set; }
        }

        public class CMakeBuild
        {
            public string? BaseDirectory { get; set; }
            public List<string> Ignore { get; set; } = [];
            public string? Command { get; set; }
        }

        public class CMakeConfig
        {
            public CMakeBuild? Build { get; set; }
            public CommandConfig? Publish { get; set; }
        }

        public class CsprojConfig
        {
            public CommandConfig? Check { get; set; }
            public CommandConfig? Publish { get; set; }
        }

        public CMakeConfig? CMake { get; set; }
        public CsprojConfig? Csproj { get; set; }
    }

    public static async Task ProcessAsync(string path)
    {
        if (path == null)
        {
            ShowErrorMessage("No URI found for clicked item.");
            return;
        }

        var config = ReadConfig(path, out string? fastBuildDirectory);
        if (fastBuildDirectory == null)
        {
            ShowErrorMessage(".fastbuild directory not found.");
            return;
        }

        if (config == null)
        {
            ShowErrorMessage(".fastbuild/config.json not found.");
            return;
        }
        
        if (config.Requires != null)
        {
            if (Version.TryParse(config.Requires, out Version? requiredVersion))
            {
                if (VersionInfo.Version < requiredVersion)
                {
                    ShowErrorMessage($"Current Fast Build version ({VersionInfo.Version}) is lower than the required version in your config ({requiredVersion}).");
                    return;
                }
            }
            else
            {
                ShowErrorMessage("Invalid required version format.");
                return;
            }
        }

        if (path.StartsWith(fastBuildDirectory))
        {
            ShowErrorMessage("File is inside .fastbuild directory.");
            return;
        }

        if (Path.GetExtension(path) != ".csproj" && Path.GetExtension(path) != ".cs")
        {
            if (await BuildAndPublishCMake(config.CMake, path, fastBuildDirectory))
                return;
        }

        await BuildAndPublishCsproj(config.Csproj, path, fastBuildDirectory);
    }

    private static async Task<bool> BuildAndPublishCMake(Config.CMakeConfig? config, string path, string fastBuildDirectory)
    {
        if (config?.Build == null)
        {
            ShowErrorMessage("CMake Build configuration not found in config.json.");
            return false;
        }

        if (config?.Publish == null)
        {
            ShowErrorMessage("CMake Publish configuration not found in config.json.");
            return false;
        }

        var cmakeBaseDirectory = config.Build.BaseDirectory;
        if (string.IsNullOrEmpty(cmakeBaseDirectory))
        {
            ShowErrorMessage("CMake base directory not found in config.json.");
            return false;
        }
        
        var buildCommand = config.Build.Command;
        if (string.IsNullOrEmpty(buildCommand))
        {
            ShowErrorMessage("Command to build cmake not found in config.json.");
            return false;
        }

        var publishCommand = config.Publish.Command;
        if (string.IsNullOrEmpty(publishCommand))
        {
            ShowErrorMessage("Command to publish cmake not found in config.json.");
            return false;
        }

        var rootDirectory = Path.GetDirectoryName(fastBuildDirectory);
        if (rootDirectory == null)
        {
            ShowErrorMessage("Failed to find root directory.");
            return false;
        }

        cmakeBaseDirectory = Path.Combine(rootDirectory, cmakeBaseDirectory);
        if (!Directory.Exists(cmakeBaseDirectory))
        {
            ShowErrorMessage($"CMake base directory not found: {cmakeBaseDirectory}");
            return false;
        }

        if (!path.StartsWith(cmakeBaseDirectory))
        {
            // Path is not inside CMake base directory
            return false;
        }

        var cache = CMakeParser.GetCache(cmakeBaseDirectory, fastBuildDirectory);

        List<CMakeParser.CMakeCache.Info> cmakeList = [];
        if (Directory.Exists(path))
        {
            cmakeList.AddRange(cache.List.Where(c => path.EndsWith(Path.GetDirectoryName(c.CMakePath) ?? "<>")));
        }
        else if (File.Exists(path))
        {
            cmakeList.AddRange(cache.List.Where(c => c.SourceFiles.Any(path.EndsWith) || path.EndsWith(c.CMakePath)));
        }

        if (cmakeList.Count == 0)
            return false;

        int i = 0;
        while (i < cmakeList.Count)
        {
            if (config.Build.Ignore.Any(cmakeList[i].ProjectName.Contains))
            {
                ShowDebugMessage($"[Ignore] {cmakeList[i].ProjectName}");
                cmakeList.RemoveAt(i);
                continue;
            }

            if (config.Build.Ignore.Any(cmakeList[i].CMakePath.Contains))
            {
                ShowDebugMessage($"[Ignore] {cmakeList[i].CMakePath}");
                cmakeList.RemoveAt(i);
                continue;
            }

            foreach (var usedBy in cmakeList[i].UsedBy)
            {
                var usedByCMake = cache.List.FirstOrDefault(c => c.ProjectName == usedBy);
                if (usedByCMake != null)
                    cmakeList.Add(usedByCMake);
            }

            i++;
        }

        foreach (var cmake in cmakeList)
        {
            var cmakeDirectory = Path.GetDirectoryName(cmake.CMakePath)
                ?? throw new Exception($"Failed to get cmake directory from {cmake.CMakePath}.");

            ShowInformationMessage($"Building CMake {cmake.ProjectName}...");
            if (!await BuildManager.BuildCMake(buildCommand, cmakeBaseDirectory, cmake.ProjectName, cmakeDirectory))
                return true; // true, it was handled
        }

        if (cmakeList.Any(c => c.Shared))
        {
            ShowInformationMessage("Publishing...");
            foreach (var cmake in cmakeList.Where(c => c.Shared))
                await BuildManager.PublishCMake(publishCommand, config.Publish.Files, fastBuildDirectory, cmake.ProjectName);
        }
        else
        {
            ShowDebugMessage("Nothing to publish, no shared project were build.");
        }

        return true;
    }

    private static async Task<bool> BuildAndPublishCsproj(Config.CsprojConfig? config, string path, string fastBuildDirectory)
    {
        if (config?.Publish == null)
        {
            ShowErrorMessage("Csproj Publish configuration not found in config.json.");
            return false;
        }

        var publishCommand = config.Publish.Command;
        if (string.IsNullOrEmpty(publishCommand))
        {
            ShowErrorMessage("Command to publish csproj not found in config.json.");
            return false;
        }

        string? csprojPath = PathFinder.FindCsprojFileAsync(path);
        if (string.IsNullOrEmpty(csprojPath))
        {
            ShowErrorMessage("No .csproj file found in parent directories.");
            return false;
        }
        
        ShowInformationMessage($"Found file: {csprojPath}.");

        var checkCommand = config.Check?.Command;
        if (!string.IsNullOrEmpty(checkCommand) && !await BuildManager.CheckCsproj(checkCommand, config.Check?.Files ?? [], fastBuildDirectory))
            return false;

        ShowInformationMessage("Creating FastBuild projects...");

        string rootDirectory = Path.GetDirectoryName(fastBuildDirectory) ?? throw new ArgumentNullException(nameof(fastBuildDirectory));
        var (fastbuildCsprojPath, packageId, anyCsprojChanged) = await CsprojProcessor.CreateCsprojFastBuildFileAsync(csprojPath, [
            new Tuple<string, string>("$(GeneXusWorkingCopy)", rootDirectory) // TODO: don't hardcode this GeneXusWorkingCopy
        ]);
        
        if (string.IsNullOrEmpty(fastbuildCsprojPath))
        {
            ShowErrorMessage("Failed to create .fastbuild.csproj file.");
            return false;
        }

        if (string.IsNullOrEmpty(packageId))
        {
            ShowErrorMessage($"Failed to get package id for {fastbuildCsprojPath}.");
            return false;
        }

        ShowInformationMessage($"Building: {fastbuildCsprojPath}...");
        if (!await BuildManager.BuildCsproj(fastbuildCsprojPath, anyCsprojChanged))
            return false;

        ShowInformationMessage($"Publishing: {packageId}...");
        return await BuildManager.PublishCsproj(publishCommand, config.Publish.Files, fastBuildDirectory, packageId);
    }

    private static JsonSerializerOptions JsonSerializerOptionsIgnoreCase => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static Config? ReadConfig(string path, out string? fastBuildDirectory)
    {
        fastBuildDirectory = PathFinder.FindFastBuildDirectory(path);
        if (string.IsNullOrEmpty(fastBuildDirectory))
            return null;

        string configFilePath = Path.Combine(fastBuildDirectory, "config.json");
        if (!File.Exists(configFilePath))
            return null;

        string configJson = File.ReadAllText(configFilePath);
        return JsonSerializer.Deserialize<Config>(configJson, JsonSerializerOptionsIgnoreCase);
    }
}
