using static OutputHandler;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args.Length == 1 && args[0] == "--help")
        {
            ShowInformationMessage("Usage: dotnet-app <path>");
            return;
        }

        if (args.Length > 1)
        {
            ShowInformationMessage("Error: Too many parameters provided.");
            return;
        }

        string path = args[0];
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            ShowInformationMessage("Error: Invalid parameter. Please provide a valid file or directory path.");
            return;
        }

        FastBuildHelper.ProcessAsync(path).Wait();
    }
}
