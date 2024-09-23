using System.Diagnostics;
using System.Runtime.InteropServices;
using static OutputHandler;

public static class BuildManager
{
    private static async Task<bool> RunCommand(string command, string workingDirectory)
    {
        ShowDebugMessage($"[RUN in {workingDirectory}] {command}");

        string fileName;
        string arguments;

        // Check if the command starts with quotes for the file name
        if (command.StartsWith('"'))
        {
            // Find the closing quote
            var closingQuoteIndex = command.IndexOf('"', 1);
            if (closingQuoteIndex == -1)
            {
                throw new ArgumentException("Invalid command format: Missing closing quote.");
            }

            // Extract the fileName between the quotes
            fileName = command[1..closingQuoteIndex];

            // The rest is arguments, if any
            if (closingQuoteIndex + 1 < command.Length)
            {
                arguments = command[(closingQuoteIndex + 2)..]; // Skip the space after the closing quote
            }
            else
            {
                arguments = string.Empty;
            }
        }
        else
        {
            // No quotes, so the first word is the file name, rest is arguments
            var firstSpaceIndex = command.IndexOf(' ');
            if (firstSpaceIndex == -1)
            {
                fileName = command;
                arguments = string.Empty;
            }
            else
            {
                fileName = command[..firstSpaceIndex];
                arguments = command[(firstSpaceIndex + 1)..];
            }
        }
        
        Process buildProcess = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        buildProcess.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) ShowOutputMessage(e.Data); };
        buildProcess.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) ShowErrorMessage(e.Data); };

        buildProcess.Start();
        buildProcess.BeginOutputReadLine();
        buildProcess.BeginErrorReadLine();
        await buildProcess.WaitForExitAsync();
        return buildProcess.ExitCode == 0;
    }

    public static async Task<bool> BuildCMake(string command, string baseDirectory, string projectName, string cmakeDirectory)
    {
        command = command
            .Replace("{{PROJECT_NAME}}", projectName)
            .Replace("{{CMAKE_DIRECTORY}}", cmakeDirectory);
        return await RunCommand(command, baseDirectory);
    }

    public static async Task<bool> BuildCsproj(string csprojPath, bool needsToRestore)
    {
        string csprojDir = Path.GetDirectoryName(csprojPath) ?? throw new ArgumentNullException(nameof(csprojPath));
        return await RunCommand($"dotnet build \"{csprojPath}\" {(needsToRestore ? "" : "--no-restore")}", csprojDir);
    }

    public static async Task<bool> CopyFilesAndRun(string command, List<string> files, string fastBuildDirectory, IList<Tuple<string, string>> replacements)
    {
        string rootDirectory = Path.GetDirectoryName(fastBuildDirectory) ?? throw new ArgumentNullException(nameof(fastBuildDirectory));
        command = command.Replace("{{ROOT_DIRECTORY}}", rootDirectory);
        command = command.Replace("{{RUNTIME_IDENTIFIER}}", RuntimeInformation.RuntimeIdentifier);

        string buildDirectory = Path.Combine(fastBuildDirectory, "_Build");
        if (!Directory.Exists(buildDirectory))
            Directory.CreateDirectory(buildDirectory);

        foreach (string fileName in files)
        {
            string filePath = Path.Combine(fastBuildDirectory, fileName);
            if (!File.Exists(filePath))
            {
                ShowErrorMessage($"File from config not found: {filePath}");
                return false;
            }

            if (fileName.Contains(".template"))
            {
                string fileContent = File.ReadAllText(filePath);
                foreach (var replacement in replacements)
                {
                    fileContent = fileContent.Replace(replacement.Item1, replacement.Item2);
                }
                string destinationFile = Path.Combine(buildDirectory, fileName.Replace(".template", ""));
                File.WriteAllText(destinationFile, fileContent);
            }
            else
            {
                string destinationFile = Path.Combine(buildDirectory, fileName);
                if (!File.Exists(destinationFile) || File.GetLastWriteTime(filePath) > File.GetLastWriteTime(destinationFile))
                {
                    File.Copy(filePath, destinationFile, true);
                }
            }
        }

        return await RunCommand(command, buildDirectory);
    }

    public static async Task<bool> PublishCMake(string command, List<string> files, string fastBuildDirectory, string projectName)
    {
        command = command.Replace("{{PROJECT_NAME}}", projectName);
        return await CopyFilesAndRun(command, files, fastBuildDirectory, []);
    }

    public static async Task<bool> CheckCsproj(string command, List<string> files, string fastBuildDirectory)
    {
        return await CopyFilesAndRun(command, files, fastBuildDirectory, []);
    }

    public static async Task<bool> PublishCsproj(string command, List<string> files, string fastBuildDirectory, string assemblyName)
    {
        return await CopyFilesAndRun(command, files, fastBuildDirectory, [
            new Tuple<string, string>("{{PACKAGE}}", assemblyName)
        ]);
    }
}
