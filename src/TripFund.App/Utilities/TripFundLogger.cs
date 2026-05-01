using System.Diagnostics;

namespace TripFund.App.Utilities;

/// <summary>
/// Simple static logger for TripFund application.
/// Prepends "[TripFund] " to all messages to allow easy filtering in logcat.
/// </summary>
public static class TripFundLogger
{
    private const string AppPrefix = "[TripFund]";

    public static void Debug(string message)
    {
        Log("DEBUG", message);
    }

    public static void Info(string message)
    {
        Log("INFO", message);
    }

    public static void Warning(string message)
    {
        Log("WARNING", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        var fullMessage = ex != null ? $"{message}: {ex}" : message;
        Log("ERROR", fullMessage);
    }

    private static void Log(string level, string message)
    {
        System.Diagnostics.Debug.WriteLine($"{AppPrefix} [{level}] {message}");
    }
}
