using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using static OutputHandler;

public static class BuildManager
{
    private static bool RunDotNet(string arguments, string workingDirectory)
    {
        Process buildProcess = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        buildProcess.OutputDataReceived += (sender, e) => ShowInformationMessage(e.Data ?? "");
        buildProcess.ErrorDataReceived += (sender, e) => ShowErrorMessage(e.Data ?? "");

        buildProcess.Start();
        buildProcess.BeginOutputReadLine();
        buildProcess.BeginErrorReadLine();
        buildProcess.WaitForExit();
        return buildProcess.ExitCode == 0;
    }

    public static bool BuildCsproj(string csprojPath)
    {
        string csprojDir = Path.GetDirectoryName(csprojPath) ?? throw new ArgumentNullException(nameof(csprojPath));
        return RunDotNet($"build \"{csprojPath}\"", csprojDir);
    }

    public static void Publish(string csprojPath)
    {
        string csprojDir = Path.GetDirectoryName(csprojPath) ?? throw new ArgumentNullException(nameof(csprojPath));
        string? fastBuildDir = FindFastBuildDir(csprojDir);

        if (fastBuildDir != null)
        {
            string fastbuildCsproj = Path.Combine(fastBuildDir, ".fastbuild", "_Build", "FastBuild.msbuild");
            if (File.Exists(fastbuildCsproj))
            {
                RunDotNet($"build \"{fastbuildCsproj}\" --no-restore", fastBuildDir);
            }
            else
            {
                ShowErrorMessage($"FastBuild.msbuild not found in {fastBuildDir}/.fastbuild/_Build directory parent of {csprojPath}");
            }
        }
        else
        {
            ShowErrorMessage($".fastbuild directory not found in current or ancestor directories of {csprojPath}");
        }
    }

    private static string? FindFastBuildDir(string dir)
    {
        if (Directory.Exists(Path.Combine(dir, ".fastbuild")))
        {
            return dir;
        }

        string parentDir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        if (parentDir == null || parentDir == dir)
        {
            return null;
        }

        return FindFastBuildDir(parentDir);
    }
}