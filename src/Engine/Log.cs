namespace Engine;

/// <summary>How important a log message is. <see cref="Log.Level"/> hides everything below it.</summary>
public enum LogLevel
{
    Info,    // what's happening: GPU in use, objects placed
    Warning, // something odd that the game recovers from
    Error,   // something failed
    None,    // not a message level: set Log.Level to this to turn logging off
}

/// <summary>
/// Console logging with three levels. Messages below <see cref="Level"/> are skipped, and
/// <see cref="LogLevel.None"/> turns logging off completely:
/// <code>Log.Level = LogLevel.Warning; // only warnings and errors</code>
/// Errors go to the error stream, so they still show when normal output is redirected.
/// </summary>
public static class Log
{
    public static LogLevel Level { get; set; } = LogLevel.Info;

    public static bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= Level;

    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    // Note: an interpolated message ($"...") is built before it gets here, even if the level is off.
    // For messages in hot paths (every frame), check IsEnabled first.
    private static void Write(LogLevel level, string message)
    {
        if (!IsEnabled(level))
            return;

        var (writer, label, color) = level switch
        {
            LogLevel.Warning => (Console.Out, "WARN ", ConsoleColor.Yellow),
            LogLevel.Error => (Console.Error, "ERROR", ConsoleColor.Red),
            _ => (Console.Out, "INFO ", Console.ForegroundColor),
        };

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        writer.WriteLine($"[{label}] {message}");
        Console.ForegroundColor = previous;
    }
}
