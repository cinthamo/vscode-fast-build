public static class OutputHandler
{
    private const string redColor = "\u001b[31m";
    private const string blueColor = "\u001b[34m";
    private const string grayColor = "\u001b[90m";
    private const string resetColor = "\u001b[0m";

    public static void ShowOutputMessage(string message)
    {
        Console.WriteLine(message);
    }

    public static void ShowInformationMessage(string message)
    {
        Console.WriteLine($"{blueColor}{message}{resetColor}");
    }

    public static void ShowDebugMessage(string message)
    {
        Console.WriteLine($"{grayColor}{message}{resetColor}");
    }

    public static void ShowErrorMessage(string message)
    {
        Console.Error.WriteLine($"{redColor}Error: {message}{resetColor}");
    }
}