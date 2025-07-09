namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Defines the severity levels for log messages.
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// Most detailed logs containing verbose information. 
    /// Should only be enabled during development or detailed troubleshooting.
    /// </summary>
    Verbose = 0,

    /// <summary>
    /// Debug information useful during development and troubleshooting.
    /// </summary>
    Debug = 1,

    /// <summary>
    /// Informational messages that track the general flow of the application.
    /// </summary>
    Information = 2,

    /// <summary>
    /// Warnings about potentially harmful situations or recoverable errors.
    /// </summary>
    Warning = 3,

    /// <summary>
    /// Error events that might still allow the application to continue running.
    /// </summary>
    Error = 4,

    /// <summary>
    /// Critical failures that require immediate attention.
    /// </summary>
    Critical = 5,

    /// <summary>
    /// No logging.
    /// </summary>
    None = 6
}

/// <summary>
/// Extension methods for LogLevel enum.
/// </summary>
public static class LogLevelExtensions
{
    /// <summary>
    /// Gets the short string representation of the log level.
    /// </summary>
    public static string ToShortString(this LogLevel level)
    {
        return level switch
        {
            LogLevel.Verbose => "VRB",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            LogLevel.None => "OFF",
            _ => level.ToString().ToUpper()
        };
    }

    /// <summary>
    /// Determines if this log level should be logged based on the minimum level.
    /// </summary>
    public static bool IsEnabled(this LogLevel level, LogLevel minimumLevel)
    {
        return level >= minimumLevel && level != LogLevel.None;
    }
}
