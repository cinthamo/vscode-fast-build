public static class OutputHandler
{
    public static void ShowInformationMessage(string message)
    {
        Console.WriteLine(message);
    }

    public static void ShowErrorMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        const string redColor = "\u001b[31m";
        const string resetColor = "\u001b[0m";
        Console.Error.WriteLine($"{redColor}Error: {message}{resetColor}");
    }
}