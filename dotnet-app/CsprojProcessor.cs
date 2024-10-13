using System.Text.Json;
using System.Xml.Linq;
using static OutputHandler;

namespace FastBuild;

public static class CsprojProcessor
{
    /// <summary>
    /// Creates or updates a .fastbuild.csproj file for a given .csproj file.
    /// </summary>
    /// <param name="csprojPath">The path to the .csproj file.</param>
    /// <param name="replacements">Variables to replace</param>
    /// <returns>A tuple containing the path to the .fastbuild.csproj file, the PackageId, and a boolean indicating whether the file was created or updated.</returns>
    public static async Task<(string?, string?, bool)> CreateCsprojFastBuildFileAsync(string csprojPath, IList<Tuple<string, string>> replacements)
    {
        string fastbuildSdkDirectory = Path.Combine(PathFinder.FindFastBuildDirectory(csprojPath)!, "_Sdk");
        Directory.CreateDirectory(fastbuildSdkDirectory);
        string fastbuildCsprojFile = csprojPath.Replace(".csproj", ".fastbuild.csproj");

        IDictionary<string, string>? globalJsonSdkVersions = null;
        string? projectDir = Path.GetDirectoryName(csprojPath);
        if (projectDir != null)
        {
            string? globalJsonPath = PathFinder.FindGlobalJsonFileAsync(projectDir);
            if (globalJsonPath != null)
            {
                string globalJsonContent = await File.ReadAllTextAsync(globalJsonPath);
                JsonDocument globalJsonDocument = JsonDocument.Parse(globalJsonContent);
                globalJsonSdkVersions = globalJsonDocument.RootElement.GetProperty("msbuild-sdks")
                    .EnumerateObject()
                    .Where(p => p.Value.GetString() != null)
                    .ToDictionary(p => p.Name, p => p.Value.GetString()!);
            }
        }

        var (packageId, wasChanged) = await CreateCsprojFastBuildFileAsync(csprojPath, new HashSet<string>(), fastbuildSdkDirectory, fastbuildCsprojFile, globalJsonSdkVersions, false, replacements);
        return (fastbuildCsprojFile, packageId, wasChanged);
    }

    private static async Task<(string?, bool)> CreateCsprojFastBuildFileAsync(string csprojPath, HashSet<string> processedFiles, string fastbuildSdkDirectory, string fastbuildCsprojFile, IDictionary<string, string>? globalJsonSdkVersions, bool isSdk, IList<Tuple<string, string>> replacements)
    {
        // Check if the file has already been processed
        if (processedFiles.Contains(csprojPath))
        {
            ShowDebugMessage($"Skipping already processed file: {csprojPath}");
            return (null, false);
        }

        // Add the file to the processed set
        processedFiles.Add(csprojPath);

        try
        {
            // Construct the output filename by replacing the extension
            string projectDir = Path.GetDirectoryName(csprojPath) ?? throw new ArgumentNullException(nameof(csprojPath));

            // Check if we need to create or update the .fastbuild file
            bool shouldFastBuildFileBeCreatedOrUpdated =
                !File.Exists(fastbuildCsprojFile) // Create if it doesn't exist
                || File.GetLastWriteTime(csprojPath) > File.GetLastWriteTime(fastbuildCsprojFile); // Update if .csproj is newer than .fastbuild

            if (shouldFastBuildFileBeCreatedOrUpdated)
                ShowDebugMessage($"Updating FastBuild project for {csprojPath}");
            else
                ShowDebugMessage($"Processing project references for {csprojPath}");

            // Read the .csproj file
            string csprojContent = await File.ReadAllTextAsync(csprojPath);
            XDocument parsedCsproj = XDocument.Parse(csprojContent);

            // Resolve reference to GeneXus SDK
            async Task<string> CreateCsprojFastBuildSdkAsync(string sdk)
            {
                if (globalJsonSdkVersions == null)
                    throw new Exception("global.json file not found");
               
                if (!globalJsonSdkVersions.TryGetValue(sdk, out var sdkVersion))
                    throw new Exception($"Version not found in global.json for {sdk}");

                string sdkPropsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", sdk.ToLower(), sdkVersion, "Sdk", "Sdk.props");
                if (!File.Exists(sdkPropsPath))
                    throw new Exception($"Sdk.props file not found for SDK: {sdk} version {sdkVersion}");

                var fastBuildSdkPropsPath = Path.Combine(fastbuildSdkDirectory, $"{sdk}.fastbuild.props");
                await CreateCsprojFastBuildFileAsync(sdkPropsPath, processedFiles, fastbuildSdkDirectory, fastBuildSdkPropsPath, globalJsonSdkVersions, true, replacements);
                return fastBuildSdkPropsPath;
            }

            if (isSdk)
            {
                foreach (var import in parsedCsproj.Root!.Elements())
                {
                    if (import.Name.LocalName != "Import") continue;
                    var projectAttribute = import.Attribute("Project");
                    if (projectAttribute?.Value != "Sdk.props") continue;
                    var importSdkAttribute = import.Attribute("Sdk");
                    if (importSdkAttribute == null) continue;

                    if (!importSdkAttribute.Value.StartsWith("Microsoft"))
                    {
                        var fastBuildSdkPropsPath = await CreateCsprojFastBuildSdkAsync(importSdkAttribute.Value);
                        projectAttribute.Value = fastBuildSdkPropsPath;
                        importSdkAttribute.Remove();
                        shouldFastBuildFileBeCreatedOrUpdated = true;
                    }
                    break;
                }
            }
            else
            {
                var projectSdk = parsedCsproj.Root?.Attributes("Sdk").FirstOrDefault()?.Value;
                if (projectSdk != null && !projectSdk.StartsWith("Microsoft."))
                {
                    parsedCsproj.Root?.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");

                    var fastBuildSdkPropsPath = await CreateCsprojFastBuildSdkAsync(projectSdk);

                    // Add import to sdk.props at the beginning
                    parsedCsproj.Root?.AddFirst(
                        new XElement("Import", new XAttribute("Project", fastBuildSdkPropsPath)));

                    // Add import to sdk.targets at the end
                    parsedCsproj.Root?.Add(new XElement("Import",
                        [new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", projectSdk)]));

                    shouldFastBuildFileBeCreatedOrUpdated = true;
                }
            }
            
            // Add the RootNamespace and AssemblyName to the project if they don't exist
            string projectName = Path.GetFileNameWithoutExtension(csprojPath);

            string? packageId = null;
            if (!isSdk)
            {
                AddProperty(parsedCsproj, "RootNamespace", projectName);
                string assemblyName = AddProperty(parsedCsproj, "AssemblyName", projectName);
                packageId = GetProperty(parsedCsproj, "PackageId") ?? assemblyName;
            }

            // Recursively process the referenced .csproj
            var projectReferences = parsedCsproj.Descendants(parsedCsproj.Root!.GetDefaultNamespace().GetName("ProjectReference"));

            foreach (var projectReference in projectReferences)
            {
                string? projectReferencePath = projectReference.Attribute("Include")?.Value.Replace("\\", "/");
                if (!string.IsNullOrEmpty(projectReferencePath))
                {
                    foreach (var replacement in replacements)
                    {
                        projectReferencePath = projectReferencePath.Replace(replacement.Item1, replacement.Item2);
                    }

                    string referencedCsprojPath = Path.GetFullPath(Path.Combine(projectDir, projectReferencePath));
                    string referencedFastBuildCsprojPath = referencedCsprojPath.Replace(".csproj", ".fastbuild.csproj");
                    projectReference.SetAttributeValue("Include", referencedFastBuildCsprojPath);

                    if (File.Exists(referencedCsprojPath) && !processedFiles.Contains(referencedCsprojPath))
                    {
                        await CreateCsprojFastBuildFileAsync(referencedCsprojPath, processedFiles, fastbuildSdkDirectory, referencedFastBuildCsprojPath, globalJsonSdkVersions, false, replacements);
                    }
                    else if (processedFiles.Contains(referencedCsprojPath))
                    {
                        ShowDebugMessage($"Skipping already processed reference: {referencedCsprojPath}");
                    }
                    else
                    {
                        ShowErrorMessage($"Referenced project does not exist: {referencedCsprojPath}");
                    }
                }
            }

            if (shouldFastBuildFileBeCreatedOrUpdated)
            {
                // Save the updated .fastbuild.csproj file
                await File.WriteAllTextAsync(fastbuildCsprojFile, parsedCsproj.ToString());
                ShowInformationMessage($"File has been saved to {fastbuildCsprojFile}");
            }

            return (packageId, shouldFastBuildFileBeCreatedOrUpdated);
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Processing the .csproj file: {ex.Message}");
            return (null, false);
        }
    }

    private static string? GetProperty(XDocument doc, string propertyName)
    {
        var propertyGroups = doc.Descendants("PropertyGroup");
        foreach (var propertyGroup in propertyGroups)
        {
            var currentValue = propertyGroup.Element(propertyName)?.Value;
            if (currentValue != null)
                return currentValue;
        }
        return null;
    }

    private static string AddProperty(XDocument doc, string propertyName, string propertyValue)
    {
        var currentValue = GetProperty(doc, propertyName);
        if (currentValue != null)
            return currentValue;

        if (doc.Root == null)
            throw new ArgumentNullException(nameof(doc));

        var newProperty = new XElement(propertyName, propertyValue);
        var firstPropertyGroup = doc.Root.Element("PropertyGroup");
        if (firstPropertyGroup == null)
        {
            firstPropertyGroup = new XElement("PropertyGroup");
            doc.Root.Add(firstPropertyGroup);
        }
        firstPropertyGroup.Add(newProperty);
        return propertyValue;
    }
}
