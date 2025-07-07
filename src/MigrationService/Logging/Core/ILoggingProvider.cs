using System;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Defines the interface for logging providers that write log entries to various targets.
/// </summary>
public interface ILoggingProvider : IDisposable
{
    /// <summary>
    /// Gets the name of the logging provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets whether the provider is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Writes a log entry asynchronously.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task WriteLogAsync(LogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any buffered log entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task FlushAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Configures the provider with the specified settings.
    /// </summary>
    /// <param name="settings">The logging settings.</param>
    void Configure(LoggingSettings settings);

    /// <summary>
    /// Checks if the provider should log entries of the specified level.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if the level should be logged; otherwise, false.</returns>
    bool IsLevelEnabled(LogLevel level);
}

/// <summary>
/// Base settings for logging configuration.
/// </summary>
public class LoggingSettings
{
    /// <summary>
    /// The minimum log level to write.
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Whether the provider is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Provider-specific settings.
    /// </summary>
    public Dictionary<string, object> ProviderSettings { get; set; } = new();

    /// <summary>
    /// Category-specific log level overrides.
    /// </summary>
    public Dictionary<string, LogLevel> CategoryOverrides { get; set; } = new();

    /// <summary>
    /// Gets the effective log level for a specific category.
    /// </summary>
    public LogLevel GetEffectiveLevel(string category)
    {
        if (string.IsNullOrEmpty(category))
            return MinimumLevel;

        // Check for exact match
        if (CategoryOverrides.TryGetValue(category, out var exactLevel))
            return exactLevel;

        // Check for partial matches (e.g., "MigrationTool.Service" matches "MigrationTool.Service.IPC")
        foreach (var (prefix, level) in CategoryOverrides)
        {
            if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return level;
        }

        return MinimumLevel;
    }
}