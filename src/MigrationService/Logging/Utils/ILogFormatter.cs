using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Utils;

/// <summary>
/// Interface for formatting log entries into string representations.
/// </summary>
public interface ILogFormatter
{
    /// <summary>
    /// Formats a log entry into a string.
    /// </summary>
    /// <param name="entry">The log entry to format.</param>
    /// <returns>The formatted string representation.</returns>
    string Format(LogEntry entry);
}