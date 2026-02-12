using System.Collections.Generic;
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
    /// <param name="compatibilityMode">Whether to use compatibility mode (skip optimizations).</param>
    /// <returns>A tuple containing the path to the .fastbuild.csproj file, the PackageId, a boolean indicating whether the file was updated, and a boolean indicating whether dependencies changed.</returns>
    public static async Task<(string?, string?, bool, bool)> CreateCsprojFastBuildFileAsync(string csprojPath, IList<Tuple<string, string>> replacements, bool compatibilityMode)
    {
        string fastbuildSdkDirectory = Path.Combine(PathFinder.FindFastBuildDirectory(csprojPath)!, "_Sdk");
        Directory.CreateDirectory(fastbuildSdkDirectory);
        string fastbuildCsprojFile = csprojPath.Replace(".csproj", ".fastbuild.csproj");

        IDictionary<string, string>? globalJsonSdkVersions = null;
        string? projectDir = Path.GetDirectoryName(csprojPath);
        string? globalJsonPath = projectDir == null ? null : PathFinder.FindGlobalJsonFileAsync(projectDir);
        if (globalJsonPath != null)
        {
            string globalJsonContent = await File.ReadAllTextAsync(globalJsonPath);
            JsonDocument globalJsonDocument = JsonDocument.Parse(globalJsonContent);
            if (globalJsonDocument.RootElement.TryGetProperty("msbuild-sdks", out JsonElement element))
            {
                globalJsonSdkVersions = element
                    .EnumerateObject()
                    .Where(p => p.Value.GetString() != null)
                    .ToDictionary(p => p.Name.ToLower(), p => p.Value.GetString()!);
            }
        }

        var (packageId, fileUpdated, depChanged) = await CreateCsprojFastBuildFileAsync(csprojPath, new HashSet<string>(), fastbuildSdkDirectory, fastbuildCsprojFile, globalJsonPath, globalJsonSdkVersions, false, replacements, compatibilityMode);
        return (fastbuildCsprojFile, packageId, fileUpdated, depChanged);
    }

    private static async Task<(string?, bool, bool)> CreateCsprojFastBuildFileAsync(string csprojPath, HashSet<string> processedFiles, string fastbuildSdkDirectory, string fastbuildCsprojFile, string? globalJsonPath, IDictionary<string, string>? globalJsonSdkVersions, bool isSdk, IList<Tuple<string, string>> replacements, bool compatibilityMode)
    {
        // Check if the file has already been processed
        if (processedFiles.Contains(csprojPath))
        {
            ShowDebugMessage($"Skipping already processed file: {csprojPath}");
            return (null, false, false);
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
               
                if (!globalJsonSdkVersions.TryGetValue(sdk.ToLower(), out var sdkVersion))
                    throw new Exception($"Version not found for {sdk} in {globalJsonPath}");

                string sdkPropsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", sdk.ToLower(), sdkVersion, "Sdk", "Sdk.props");
                if (!File.Exists(sdkPropsPath))
                {
                    ShowDebugMessage($"File not found: {sdkPropsPath}, trying to restore it...");
                    var fastBuildSdkCsprojPath = Path.Combine(fastbuildSdkDirectory, $"{sdk}.fastbuild.csproj");
                    File.WriteAllText(fastBuildSdkCsprojPath, $"<Project Sdk=\"{sdk}\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
                    await BuildManager.RestoreCsproj(fastBuildSdkCsprojPath, fastbuildSdkDirectory);
                    if (!File.Exists(sdkPropsPath))
                        throw new Exception($"File not found: {sdkPropsPath}");
                }

                var fastBuildSdkPropsPath = Path.Combine(fastbuildSdkDirectory, $"{sdk}.fastbuild.props");
                await CreateCsprojFastBuildFileAsync(sdkPropsPath, processedFiles, fastbuildSdkDirectory, fastBuildSdkPropsPath, globalJsonPath, globalJsonSdkVersions, true, replacements, compatibilityMode);
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

                // Apply performance optimizations
                if (!compatibilityMode)
                {
                    SetProperty(parsedCsproj, "TreatWarningsAsErrors", "false");
                    RemoveProperty(parsedCsproj, "ProduceReferenceAssembly");
                    SetProperty(parsedCsproj, "RestoreUseStaticGraphEvaluation", "true");
                    SetProperty(parsedCsproj, "IsPackable", "false");
                }
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
                        await CreateCsprojFastBuildFileAsync(referencedCsprojPath, processedFiles, fastbuildSdkDirectory, referencedFastBuildCsprojPath, globalJsonPath, globalJsonSdkVersions, false, replacements, compatibilityMode);
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

            // Check if PackageReference changed between new and previous .fastbuild.csproj
            string newContent = parsedCsproj.ToString();
            bool anyDependencyChanged = !File.Exists(fastbuildCsprojFile);
            if (!anyDependencyChanged)
            {
                string existingContent = await File.ReadAllTextAsync(fastbuildCsprojFile);
                var existingPackages = getPackagesAndVersion(existingContent);
                var newPackages = getPackagesAndVersion(newContent);
                anyDependencyChanged = !newPackages.SequenceEqual(existingPackages);
            }

            if (shouldFastBuildFileBeCreatedOrUpdated)
            {
                // Save the updated .fastbuild.csproj file
                await File.WriteAllTextAsync(fastbuildCsprojFile, newContent);
                ShowInformationMessage($"File has been saved to {fastbuildCsprojFile}");
            }

            return (packageId, shouldFastBuildFileBeCreatedOrUpdated, anyDependencyChanged);
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Processing the .csproj file: {ex.Message}");
            return (null, false, false);
        }

        List<string> getPackagesAndVersion(string csprojContent)
        {
            XDocument doc = XDocument.Parse(csprojContent);
            var ns = doc.Root!.GetDefaultNamespace();
            return doc.Descendants(ns.GetName("PackageReference"))
                .Select(pr => $"{pr.Attribute("Include")?.Value ?? ""}@{pr.Attribute("Version")?.Value ?? pr.Element(ns.GetName("Version"))?.Value ?? ""}")
                .OrderBy(s => s)
                .ToList();
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

    private static void SetProperty(XDocument doc, string propertyName, string propertyValue)
    {
        var propertyGroups = doc.Descendants("PropertyGroup");
        foreach (var propertyGroup in propertyGroups)
        {
            var element = propertyGroup.Element(propertyName);
            if (element != null)
            {
                element.Value = propertyValue;
                return;
            }
        }
        // If not found, add to first PropertyGroup
        var firstPropertyGroup = doc.Root?.Element("PropertyGroup");
        if (firstPropertyGroup == null)
        {
            firstPropertyGroup = new XElement("PropertyGroup");
            doc.Root!.Add(firstPropertyGroup);
        }
        firstPropertyGroup.Add(new XElement(propertyName, propertyValue));
    }

    private static void RemoveProperty(XDocument doc, string propertyName)
    {
        var propertyGroups = doc.Descendants("PropertyGroup");
        foreach (var propertyGroup in propertyGroups)
        {
            var element = propertyGroup.Element(propertyName);
            if (element != null)
            {
                element.Remove();
                break; // Remove only the first occurrence
            }
        }
    }
}
