﻿using static OutputHandler;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args.Length == 1 && args[0] == "--help")
        {
            ShowOutputMessage("Usage: dotnet-app <path>");
            return;
        }

        if (args.Length > 1)
        {
            ShowErrorMessage("Too many parameters provided.");
            return;
        }

        string path = args[0];
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            ShowErrorMessage("Invalid parameter. Please provide a valid file or directory path.");
            return;
        }

        FastBuildHelper.ProcessAsync(path).Wait();
    }
}
