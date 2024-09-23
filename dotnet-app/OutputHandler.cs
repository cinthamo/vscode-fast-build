using System.Text.RegularExpressions;

public static partial class OutputHandler
{
    private const string redColor = "\u001b[31m";
    private const string yellowColor = "\u001b[33m";
    private const string blueColor = "\u001b[34m";
    private const string grayColor = "\u001b[90m";
    private const string resetColor = "\u001b[0m";

    public static void ShowOutputMessage(string message)
    {
        var warningResultMatch = WarningResultRegex().Match(message);
        if (WarningRegex().IsMatch(message) || warningResultMatch.Success && warningResultMatch.Groups[1].Value != "0")
        {
            Console.WriteLine($"{yellowColor}{message}{resetColor}");
            return;
        }

        var errorResultMatch = ErrorResultRegex().Match(message);
        if (ErrorRegex().IsMatch(message) || errorResultMatch.Success && errorResultMatch.Groups[1].Value != "0" || message.Contains(": error :"))
        {
            Console.Error.WriteLine($"{redColor}{message}{resetColor}");
            return;
        }

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

    [GeneratedRegex(@": warning [A-Z]{2,3}\d{4}:")]
    private static partial Regex WarningRegex();

    [GeneratedRegex(@": error [A-Z]{2,3}\d{4}:")]
    private static partial Regex ErrorRegex();

    [GeneratedRegex(@"^    (\d+) Warning\(s\)$")]
    private static partial Regex WarningResultRegex();

    [GeneratedRegex(@"^    (\d+) Error\(s\)$")]
    private static partial Regex ErrorResultRegex();
}