using System.Xml.Linq;
using static OutputHandler;

public static class CsprojProcessor
{
    /// <summary>
    /// Creates or updates a .fastbuild.csproj file for a given .csproj file.
    /// </summary>
    /// <param name="csprojFile">The path to the .csproj file.</param>
    /// <param name="processedFiles">A set of already processed files.</param>
    /// <returns>A tuple containing the path to the .fastbuild.csproj file, the PackageId, and a boolean indicating whether the file was created or updated.</returns>
    public static async Task<(string?, string?, bool)> CreateCsprojFastBuildFileAsync(string csprojFile, HashSet<string> processedFiles, IList<Tuple<string, string>> replacements)
    {
        // Check if the file has already been processed
        if (processedFiles.Contains(csprojFile))
        {
            ShowDebugMessage($"Skipping already processed file: {csprojFile}");
            return (csprojFile.Replace(".csproj", ".fastbuild.csproj"), null, false);
        }

        // Add the file to the processed set
        processedFiles.Add(csprojFile);

        try
        {
            // Construct the output filename by replacing the extension
            string fastbuildFile = csprojFile.Replace(".csproj", ".fastbuild.csproj");

            // Check if we need to create or update the .fastbuild file
            bool shouldFastBuildFileBeCreatedOrUpdated =
                !File.Exists(fastbuildFile) // Create if it doesn't exist
                || File.GetLastWriteTime(csprojFile) > File.GetLastWriteTime(fastbuildFile); // Update if .csproj is newer than .fastbuild

            if (shouldFastBuildFileBeCreatedOrUpdated)
                ShowDebugMessage($"Updating FastBuild project for {csprojFile}");
            else
                ShowDebugMessage($"Processing project references for {csprojFile}");

            // Read the .csproj file
            string csprojContent = await File.ReadAllTextAsync(csprojFile);
            XDocument parsedCsproj = XDocument.Parse(csprojContent);

            // Add the RootNamespace and AssemblyName to the project if they don't exist
            string projectName = Path.GetFileNameWithoutExtension(csprojFile);

            AddProperty(parsedCsproj, "RootNamespace", projectName);
            string packageId = AddProperty(parsedCsproj, "AssemblyName", projectName);
            packageId = GetProperty(parsedCsproj, "PackageId") ?? packageId;

            // Recursively process the referenced .csproj
            var projectReferences = parsedCsproj.Descendants("ProjectReference");
            string projectDir = Path.GetDirectoryName(csprojFile) ?? throw new ArgumentNullException(nameof(csprojFile));

            foreach (var projectReference in projectReferences)
            {
                string? projectReferencePath = projectReference.Attribute("Include")?.Value?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(projectReferencePath))
                {
                    foreach (var replacement in replacements)
                    {
                        projectReferencePath = projectReferencePath.Replace(replacement.Item1, replacement.Item2);
                    }

                    string referencedCsprojPath = Path.GetFullPath(Path.Combine(projectDir, projectReferencePath));
                    projectReference.SetAttributeValue("Include", referencedCsprojPath.Replace(".csproj", ".fastbuild.csproj"));

                    if (File.Exists(referencedCsprojPath) && !processedFiles.Contains(referencedCsprojPath))
                    {
                        await CreateCsprojFastBuildFileAsync(referencedCsprojPath, processedFiles, replacements);
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
                await File.WriteAllTextAsync(fastbuildFile, parsedCsproj.ToString());
                ShowInformationMessage($"File has been saved to {fastbuildFile}");
            }

            return (fastbuildFile, packageId, shouldFastBuildFileBeCreatedOrUpdated);
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Processing the .csproj file: {ex.Message}");
            return (null, null, false);
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

    public static string? FindCsprojFileAsync(string filePath)
    {
        string dir = Path.GetDirectoryName(filePath) ?? throw new ArgumentNullException(nameof(filePath));

        while (dir != Path.GetPathRoot(dir))
        {
            string[] files = Directory.GetFiles(dir);
            string? csproj = Array.Find(files, file => file.EndsWith(".csproj") && !file.EndsWith(".fastbuild.csproj"));

            if (csproj != null)
            {
                return Path.Combine(dir, csproj);
            }

            // Go up one directory
            dir = Path.GetDirectoryName(dir) ?? throw new ArgumentNullException(nameof(filePath));
        }

        return null;
    }
}
