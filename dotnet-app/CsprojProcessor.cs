using System.Xml.Linq;
using static OutputHandler;

public static class CsprojProcessor
{
    public static async Task<string?> CreateCsprojFastBuildFileAsync(string csprojFile, HashSet<string> processedFiles)
    {
        // Check if the file has already been processed
        if (processedFiles.Contains(csprojFile))
        {
            ShowInformationMessage($"Skipping already processed file: {csprojFile}");
            return csprojFile.Replace(".csproj", ".fastbuild.csproj");
        }

        // Add the file to the processed set
        processedFiles.Add(csprojFile);

        try
        {
            // Construct the output filename by replacing the extension
            string fastbuildFile = csprojFile.Replace(".csproj", ".fastbuild.csproj");

            // Check if we need to create or update the .fastbuild file
            bool shouldFastBuildFileBeCreatedOrUpdated = true ||
                !File.Exists(fastbuildFile) // Create if it doesn't exist
                || File.GetLastWriteTime(csprojFile) > File.GetLastWriteTime(fastbuildFile); // Update if .csproj is newer than .fastbuild

            if (shouldFastBuildFileBeCreatedOrUpdated)
            {
                ShowInformationMessage($"Updating FastBuild project for {csprojFile}");
            }
            else
            {
                ShowInformationMessage($"Processing project references for {csprojFile}");
            }

            // Read the .csproj file
            string csprojContent = await File.ReadAllTextAsync(csprojFile);
            XDocument parsedCsproj = XDocument.Parse(csprojContent);

            // Add the RootNamespace and AssemblyName to the project if they don't exist
            string projectName = Path.GetFileNameWithoutExtension(csprojFile);
            var propertyGroupElements = parsedCsproj.Descendants("PropertyGroup");

            AddProperty(parsedCsproj, propertyGroupElements, "RootNamespace", projectName);
            AddProperty(parsedCsproj, propertyGroupElements, "AssemblyName", projectName);

            // Recursively process the referenced .csproj
            var projectReferences = parsedCsproj.Descendants("ProjectReference");
            string projectDir = Path.GetDirectoryName(csprojFile) ?? throw new ArgumentNullException(nameof(csprojFile));

            foreach (var projectReference in projectReferences)
            {
                string? relativePath = projectReference.Attribute("Include")?.Value?.Replace("\\", "/");
                if (!string.IsNullOrEmpty(relativePath))
                {
                    string referencedCsprojPath = Path.GetFullPath(Path.Combine(projectDir, relativePath));
                    projectReference.SetAttributeValue("Include", referencedCsprojPath.Replace(".csproj", ".fastbuild.csproj"));

                    if (File.Exists(referencedCsprojPath) && !processedFiles.Contains(referencedCsprojPath))
                    {
                        await CreateCsprojFastBuildFileAsync(referencedCsprojPath, processedFiles);
                    }
                    else if (processedFiles.Contains(referencedCsprojPath))
                    {
                        ShowInformationMessage($"Skipping already processed reference: {referencedCsprojPath}");
                    }
                    else
                    {
                        ShowInformationMessage($"Referenced project does not exist: {referencedCsprojPath}");
                    }
                }
            }

            if (shouldFastBuildFileBeCreatedOrUpdated)
            {
                // Save the updated .fastbuild.csproj file
                await File.WriteAllTextAsync(fastbuildFile, parsedCsproj.ToString());
                ShowInformationMessage($"File has been saved to {fastbuildFile}");
            }

            return fastbuildFile;
        }
        catch (Exception ex)
        {
            ShowErrorMessage($"Processing the .csproj file: {ex.Message}");
            return null;
        }
    }

    private static void AddProperty(XDocument doc, IEnumerable<XElement> propertyGroups, string propertyName, string propertyValue)
    {
        bool propertyExists = false;

        foreach (var propertyGroup in propertyGroups)
        {
            if (propertyGroup.Element(propertyName) != null)
            {
                propertyExists = true;
                break;
            }
        }

        if (!propertyExists)
        {
            var newProperty = new XElement(propertyName, propertyValue);
            doc.Root?.Element("PropertyGroup")?.Add(newProperty);
        }
    }

    public static string FindCsprojFileAsync(string filePath)
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

        throw new FileNotFoundException("No .csproj file found in parent directories.");
    }
}
