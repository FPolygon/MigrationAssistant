using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Rotation;

namespace MigrationTool.Service.Logging.Configuration;

/// <summary>
/// Configuration class for the logging system.
/// </summary>
public class LoggingConfiguration
{
    /// <summary>
    /// Global logging settings.
    /// </summary>
    public GlobalLoggingSettings Global { get; set; } = new();

    /// <summary>
    /// Provider-specific configurations.
    /// </summary>
    public Dictionary<string, ProviderConfiguration> Providers { get; set; } = new();

    /// <summary>
    /// Category-specific log level overrides.
    /// </summary>
    public Dictionary<string, LogLevel> CategoryOverrides { get; set; } = new();

    /// <summary>
    /// Performance monitoring settings.
    /// </summary>
    public PerformanceSettings Performance { get; set; } = new();

    /// <summary>
    /// Creates default logging configuration.
    /// </summary>
    /// <returns>A default logging configuration.</returns>
    public static LoggingConfiguration CreateDefault()
    {
        return new LoggingConfiguration
        {
            Global = new GlobalLoggingSettings
            {
                MinimumLevel = LogLevel.Information,
                EnableContextFlow = true,
                EnablePerformanceLogging = true
            },
            Providers = new Dictionary<string, ProviderConfiguration>
            {
                ["FileProvider"] = new FileProviderConfiguration
                {
                    Enabled = true,
                    LogDirectory = @"C:\ProgramData\MigrationTool\Logs",
                    FilePrefix = "migration",
                    MaxFileSizeBytes = 10 * 1024 * 1024, // 10MB
                    RotationInterval = RotationInterval.Daily,
                    RetentionDays = 30,
                    UseJsonFormat = false
                },
                ["EventLogProvider"] = new EventLogProviderConfiguration
                {
                    Enabled = true,
                    Source = "MigrationTool",
                    LogName = "Application",
                    MinimumLevel = LogLevel.Warning // Only log warnings and errors to Event Log
                }
            },
            CategoryOverrides = new Dictionary<string, LogLevel>
            {
                ["MigrationTool.Service.IPC"] = LogLevel.Debug,
                ["MigrationTool.Backup"] = LogLevel.Information,
                ["MigrationTool.Performance"] = LogLevel.Verbose
            }
        };
    }
}

/// <summary>
/// Global logging settings that apply to all providers.
/// </summary>
public class GlobalLoggingSettings
{
    /// <summary>
    /// The minimum log level for all providers.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Whether to enable automatic context flow (correlation IDs, user context).
    /// </summary>
    public bool EnableContextFlow { get; set; } = true;

    /// <summary>
    /// Whether to enable performance logging.
    /// </summary>
    public bool EnablePerformanceLogging { get; set; } = true;

    /// <summary>
    /// Whether to log to console in debug builds.
    /// </summary>
    public bool EnableConsoleLogging { get; set; } = false;

    /// <summary>
    /// Maximum number of log entries to buffer before forcing a flush.
    /// </summary>
    public int MaxBufferSize { get; set; } = 1000;

    /// <summary>
    /// Maximum time to wait before flushing buffered entries (in milliseconds).
    /// </summary>
    public int FlushIntervalMs { get; set; } = 5000;
}

/// <summary>
/// Base class for provider-specific configurations.
/// </summary>
public abstract class ProviderConfiguration
{
    /// <summary>
    /// Whether the provider is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum log level for this provider (overrides global setting).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel? MinimumLevel { get; set; }

    /// <summary>
    /// Provider-specific settings.
    /// </summary>
    public Dictionary<string, object> Settings { get; set; } = new();

    /// <summary>
    /// Converts this configuration to LoggingSettings for the provider.
    /// </summary>
    /// <param name="globalSettings">Global logging settings.</param>
    /// <param name="categoryOverrides">Category overrides.</param>
    /// <returns>LoggingSettings for the provider.</returns>
    public virtual LoggingSettings ToLoggingSettings(GlobalLoggingSettings globalSettings,
        Dictionary<string, LogLevel> categoryOverrides)
    {
        var settings = new LoggingSettings
        {
            Enabled = Enabled,
            MinimumLevel = MinimumLevel ?? globalSettings.MinimumLevel,
            CategoryOverrides = new Dictionary<string, LogLevel>(categoryOverrides),
            ProviderSettings = new Dictionary<string, object>(Settings)
        };

        return settings;
    }
}

/// <summary>
/// Configuration for the file logging provider.
/// </summary>
public class FileProviderConfiguration : ProviderConfiguration
{
    /// <summary>
    /// Directory where log files are stored.
    /// </summary>
    public string LogDirectory { get; set; } = @"C:\ProgramData\MigrationTool\Logs";

    /// <summary>
    /// Prefix for log file names.
    /// </summary>
    public string FilePrefix { get; set; } = "migration";

    /// <summary>
    /// Maximum size of a log file before rotation (in bytes).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024; // 10MB

    /// <summary>
    /// Time-based rotation interval.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RotationInterval RotationInterval { get; set; } = RotationInterval.Daily;

    /// <summary>
    /// Number of days to retain log files.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Whether to use JSON format instead of plain text.
    /// </summary>
    public bool UseJsonFormat { get; set; } = false;

    /// <summary>
    /// Whether to use UTC timestamps.
    /// </summary>
    public bool UseUtc { get; set; } = true;

    /// <summary>
    /// Whether to include detailed timestamps in file names.
    /// </summary>
    public bool IncludeTimestamp { get; set; } = false;

    public override LoggingSettings ToLoggingSettings(GlobalLoggingSettings globalSettings,
        Dictionary<string, LogLevel> categoryOverrides)
    {
        var settings = base.ToLoggingSettings(globalSettings, categoryOverrides);

        // Add file-specific settings
        settings.ProviderSettings["LogDirectory"] = LogDirectory;
        settings.ProviderSettings["FilePrefix"] = FilePrefix;
        settings.ProviderSettings["MaxFileSizeBytes"] = MaxFileSizeBytes;
        settings.ProviderSettings["RotationInterval"] = RotationInterval;
        settings.ProviderSettings["RetentionDays"] = RetentionDays;
        settings.ProviderSettings["UseJsonFormat"] = UseJsonFormat;
        settings.ProviderSettings["UseUtc"] = UseUtc;
        settings.ProviderSettings["IncludeTimestamp"] = IncludeTimestamp;

        return settings;
    }
}

/// <summary>
/// Configuration for the Windows Event Log provider.
/// </summary>
public class EventLogProviderConfiguration : ProviderConfiguration
{
    /// <summary>
    /// The event source name.
    /// </summary>
    public string Source { get; set; } = "MigrationTool";

    /// <summary>
    /// The event log name (usually "Application").
    /// </summary>
    public string LogName { get; set; } = "Application";

    /// <summary>
    /// The machine name for the event log.
    /// </summary>
    public string MachineName { get; set; } = ".";

    /// <summary>
    /// Maximum length of a single event message.
    /// </summary>
    public int MaxMessageLength { get; set; } = 31839;

    public override LoggingSettings ToLoggingSettings(GlobalLoggingSettings globalSettings,
        Dictionary<string, LogLevel> categoryOverrides)
    {
        var settings = base.ToLoggingSettings(globalSettings, categoryOverrides);

        // Add event log specific settings
        settings.ProviderSettings["Source"] = Source;
        settings.ProviderSettings["LogName"] = LogName;
        settings.ProviderSettings["MachineName"] = MachineName;
        settings.ProviderSettings["MaxMessageLength"] = MaxMessageLength;

        return settings;
    }
}

/// <summary>
/// Configuration for console logging provider.
/// </summary>
public class ConsoleProviderConfiguration : ProviderConfiguration
{
    /// <summary>
    /// Whether to use colored output.
    /// </summary>
    public bool UseColors { get; set; } = true;

    /// <summary>
    /// Whether to include timestamps.
    /// </summary>
    public bool IncludeTimestamp { get; set; } = true;

    /// <summary>
    /// Whether to include category information.
    /// </summary>
    public bool IncludeCategory { get; set; } = true;

    public override LoggingSettings ToLoggingSettings(GlobalLoggingSettings globalSettings,
        Dictionary<string, LogLevel> categoryOverrides)
    {
        var settings = base.ToLoggingSettings(globalSettings, categoryOverrides);

        settings.ProviderSettings["UseColors"] = UseColors;
        settings.ProviderSettings["IncludeTimestamp"] = IncludeTimestamp;
        settings.ProviderSettings["IncludeCategory"] = IncludeCategory;

        return settings;
    }
}

/// <summary>
/// Performance monitoring settings.
/// </summary>
public class PerformanceSettings
{
    /// <summary>
    /// Whether to enable performance monitoring.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum duration (in milliseconds) to log performance metrics.
    /// </summary>
    public double MinimumDurationMs { get; set; } = 100;

    /// <summary>
    /// Whether to collect memory usage metrics.
    /// </summary>
    public bool CollectMemoryMetrics { get; set; } = false;

    /// <summary>
    /// Whether to collect CPU usage metrics.
    /// </summary>
    public bool CollectCpuMetrics { get; set; } = false;

    /// <summary>
    /// Interval for collecting system metrics (in seconds).
    /// </summary>
    public int MetricsIntervalSeconds { get; set; } = 60;
}