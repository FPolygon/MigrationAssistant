using System;
using System.Collections.Generic;

namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Represents a structured log entry with all relevant information.
/// </summary>
public class LogEntry
{
    /// <summary>
    /// Unique identifier for this log entry.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the log entry was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// The severity level of this log entry.
    /// </summary>
    public LogLevel Level { get; set; }

    /// <summary>
    /// The main log message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The category or source of the log entry (e.g., component name).
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Exception information if this log entry is related to an error.
    /// </summary>
    public Exception? Exception { get; set; }

    /// <summary>
    /// Correlation ID to trace related operations across components.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// User context if applicable (user ID, session ID, etc.).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The machine name where the log was generated.
    /// </summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>
    /// The process ID that generated the log.
    /// </summary>
    public int ProcessId { get; set; } = Environment.ProcessId;

    /// <summary>
    /// The thread ID that generated the log.
    /// </summary>
    public int ThreadId { get; set; } = Environment.CurrentManagedThreadId;

    /// <summary>
    /// Additional properties for structured logging.
    /// </summary>
    public Dictionary<string, object?> Properties { get; set; } = new();

    /// <summary>
    /// Performance metrics if this is a performance-related log.
    /// </summary>
    public PerformanceMetrics? Performance { get; set; }
}

/// <summary>
/// Performance metrics associated with a log entry.
/// </summary>
public class PerformanceMetrics
{
    /// <summary>
    /// Duration of the operation in milliseconds.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Memory usage in bytes.
    /// </summary>
    public long? MemoryBytes { get; set; }

    /// <summary>
    /// CPU usage percentage.
    /// </summary>
    public double? CpuPercent { get; set; }

    /// <summary>
    /// Number of items processed.
    /// </summary>
    public int? ItemCount { get; set; }

    /// <summary>
    /// Custom metrics.
    /// </summary>
    public Dictionary<string, double> CustomMetrics { get; set; } = new();
}
