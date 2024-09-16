using static OutputHandler;

public static class FastBuildHelper
{
    public static async Task ProcessAsync(string path)
    {
        if (path != null)
        {
            string csprojPath = CsprojProcessor.FindCsprojFileAsync(path);
            if (!string.IsNullOrEmpty(csprojPath))
            {
                ShowInformationMessage($"Found .csproj file: {csprojPath}. Creating FastBuild projects...");

                var processedFiles = new HashSet<string>();
                string? fastbuildCsprojPath = await CsprojProcessor.CreateCsprojFastBuildFileAsync(csprojPath, processedFiles);
                
                if (!string.IsNullOrEmpty(fastbuildCsprojPath))
                {
                    ShowInformationMessage($"Building: {fastbuildCsprojPath}...");
                    if (BuildManager.BuildCsproj(fastbuildCsprojPath))
                    {
                        ShowInformationMessage($"Publishing: {fastbuildCsprojPath}...");
                        BuildManager.Publish(fastbuildCsprojPath);
                    }
                }
                else
                {
                    ShowErrorMessage("Failed to create .fastbuild file.");
                }
            }
            else
            {
                ShowErrorMessage("No .csproj file found in parent directories.");
            }
        }
        else
        {
            ShowErrorMessage("No URI found for clicked item.");
        }
    }
}
