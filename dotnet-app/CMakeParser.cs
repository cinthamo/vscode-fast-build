using System.Text.Json;
using System.Text.RegularExpressions;
using static OutputHandler;

partial class CMakeParser
{
    private const int CURRENT_CACHE_SCHEMA = 1;

    private class CMakeInfo
    {
        public string CMakePath { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string LibraryType { get; set; } = "";
        public List<string> SourceFiles { get; set; } = [];
        public List<string> Libraries { get; set; } = [];
    }
    
    public class CMakeCache
    {
        public class Info
        {
            public string CMakePath { get; set; } = "";
            public string ProjectName { get; set; } = "";
            public bool Shared { get; set; } = false;
            public List<string> SourceFiles { get; set; } = [];
            public List<string> UsedBy { get; set; } = [];
        }
        public List<Info> List { get; set; } = [];
        public int Schema { get; set; } = 0;
    }

    private static readonly JsonSerializerOptions JsonSerializerOptionsIndented = new() { WriteIndented = true };
    public static CMakeCache GetCache(string cmakeBaseDirectory, string fastBuildDirectory)
    {
        CMakeCache cache = new();
        string tmpDirectory = Path.Combine(fastBuildDirectory, "_Tmp");
        string cacheFile = Path.Combine(tmpDirectory, "CMakeCache.json");
        if (File.Exists(cacheFile))
            cache = JsonSerializer.Deserialize<CMakeCache>(File.ReadAllText(cacheFile)) ?? cache;

        if (cache.Schema != CURRENT_CACHE_SCHEMA)
        {
            ShowInformationMessage("Reading CMakeLists.txt files...");
            var list = Directory.GetFiles(cmakeBaseDirectory, "CMakeLists.txt", SearchOption.AllDirectories);
            if (list.Length == 0)
            {
                ShowErrorMessage("No CMakeLists.txt file found in CMake base directory.");
                return cache;
            }

            string ToRelative(string path)
            {
                if (path.StartsWith(cmakeBaseDirectory))
                {
                    path = path[cmakeBaseDirectory.Length..];
                    if (path.StartsWith(Path.DirectorySeparatorChar))
                        path = path[1..];
                }
                return path;
            }

            var infoList = list.Select(ParseCMakeLists).ToList();
            cache = new CMakeCache()
            {
                Schema = CURRENT_CACHE_SCHEMA,
                List = infoList.Select(info => new CMakeCache.Info()
                {
                    CMakePath = ToRelative(info.CMakePath),
                    ProjectName = info.ProjectName,
                    Shared = info.LibraryType == "SHARED",
                    SourceFiles = info.SourceFiles.Select(f =>
                        ToRelative(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(info.CMakePath) ?? string.Empty, f)))
                    ).ToList(),
                    UsedBy = infoList.Where(o => o.Libraries.Contains(info.ProjectName)).Select(o => o.ProjectName).ToList()
                }).ToList()
            };

            if (!Directory.Exists(tmpDirectory))
                Directory.CreateDirectory(tmpDirectory);
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(cache, JsonSerializerOptionsIndented));
        }
        return cache;
    }

    private static CMakeInfo ParseCMakeLists(string cmakePath)
    {
        if (!File.Exists(cmakePath))
            throw new FileNotFoundException("CMakeLists.txt file not found.", cmakePath);

        ShowDebugMessage($"Parsing {cmakePath}");

        // Read the CMakeLists.txt file
        string[] lines = File.ReadAllLines(cmakePath);

        // Regular expressions for extracting information
        Regex sourceFileRegex = SourceFileRegex();
        Regex targetLinkLibrariesStartRegex = TargetLinkLibrariesStartRegex();
        Regex setVariableRegex = SetVariableRegex();
        Regex addLibraryRegex = AddLibraryRegex();

        CMakeInfo cmakeInfo = new()
        {
            CMakePath = cmakePath
        };
        Dictionary<string, string> variables = [];
        bool insideLibrariesBlock = false;
        foreach (var line in lines)
        {
            // Remove comments and unnecessary whitespace
            var cleanLine = line.Split('#')[0].Trim();
            if (string.IsNullOrWhiteSpace(cleanLine)) continue;

            // Find linked libraries
            if (targetLinkLibrariesStartRegex.IsMatch(cleanLine))
            {
                insideLibrariesBlock = true;
                continue;
            }
            if (insideLibrariesBlock)
            {
                if (cleanLine.Contains(')'))
                {
                    insideLibrariesBlock = false;
                    continue;
                }

                if (cleanLine[0] != '-' && cleanLine[0] != '$')
                    cmakeInfo.Libraries.Add(cleanLine);
            }

            // Find variables
            var setVariableMatch = setVariableRegex.Match(cleanLine);
            if (setVariableMatch.Success)
            {
                string variableName = setVariableMatch.Groups[1].Value.Trim();
                string variableValue = setVariableMatch.Groups[2].Value.Trim();
                if (variableValue.StartsWith('"') && variableValue.EndsWith('"'))
                    variableValue = variableValue[1..^1];
                variables[variableName] = variableValue;
                continue;
            }

            // Replace variables
            var useVariableMatch = UseVariableRegex().Match(cleanLine);
            while (useVariableMatch.Success)
            {
                string variableName = useVariableMatch.Groups[1].Value.Trim();
                if (variables.TryGetValue(variableName, out string? value))
                    cleanLine = cleanLine.Replace($"${{{variableName}}}", value);
                else
                    break;
                useVariableMatch = UseVariableRegex().Match(cleanLine);
            }

            // Find source files
            var sourceFileMatch = sourceFileRegex.Match(cleanLine);
            if (sourceFileMatch.Success)
            {
                if (sourceFileMatch.Groups[1].Value[0] != '$')
                    cmakeInfo.SourceFiles.Add(sourceFileMatch.Groups[1].Value);
                continue;
            }

            // Find project name and library type (STATIC or SHARED)
            var addLibraryMatch = addLibraryRegex.Match(cleanLine);
            if (addLibraryMatch.Success)
            {
                cmakeInfo.ProjectName = addLibraryMatch.Groups[1].Value.Trim();;
                cmakeInfo.LibraryType = addLibraryMatch.Groups[2].Value.Trim();
            }
        }

        return cmakeInfo;
    }

    // Regex for source files
    [GeneratedRegex("\"(.*\\.\\w+)\"", RegexOptions.IgnoreCase, "en-ES")]
    private static partial Regex SourceFileRegex();

    // Regex for target_link_libraries block (multiline)
    [GeneratedRegex(@"target_link_libraries\s*\(", RegexOptions.IgnoreCase, "en-ES")]
    private static partial Regex TargetLinkLibrariesStartRegex();

    // Regex for set command to capture variables
    [GeneratedRegex(@"set\s*\(\s*(\w+)\s+(.+)\s*\)", RegexOptions.IgnoreCase, "en-ES")]
    private static partial Regex SetVariableRegex();

    // Regex for use of variables
    [GeneratedRegex(@"\$\{(\w+)\}", RegexOptions.IgnoreCase, "en-ES")]
    private static partial Regex UseVariableRegex();

    // Regex for add_library (to capture project name and library type: STATIC or SHARED)
    [GeneratedRegex(@"add_library\s*\(\s*(.+)\s+(STATIC|SHARED)\s+", RegexOptions.IgnoreCase, "en-ES")]
    private static partial Regex AddLibraryRegex();
}
