using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Extension methods for structured logging with strongly-typed properties.
/// </summary>
public static class StructuredLoggingExtensions
{
    /// <summary>
    /// Logs an information message with structured properties.
    /// </summary>
    public static Task LogInformationAsync(this ILogger logger, string message, object? properties = null)
    {
        return LogWithPropertiesAsync(logger, LogLevel.Information, message, properties);
    }

    /// <summary>
    /// Logs a warning message with structured properties.
    /// </summary>
    public static Task LogWarningAsync(this ILogger logger, string message, object? properties = null)
    {
        return LogWithPropertiesAsync(logger, LogLevel.Warning, message, properties);
    }

    /// <summary>
    /// Logs an error message with structured properties.
    /// </summary>
    public static Task LogErrorAsync(this ILogger logger, string message, Exception? exception = null, object? properties = null)
    {
        return LogWithPropertiesAsync(logger, LogLevel.Error, message, properties, exception);
    }

    /// <summary>
    /// Logs a debug message with structured properties.
    /// </summary>
    public static Task LogDebugAsync(this ILogger logger, string message, object? properties = null)
    {
        return LogWithPropertiesAsync(logger, LogLevel.Debug, message, properties);
    }

    /// <summary>
    /// Logs a critical error with structured properties.
    /// </summary>
    public static Task LogCriticalAsync(this ILogger logger, string message, Exception? exception = null, object? properties = null)
    {
        return LogWithPropertiesAsync(logger, LogLevel.Critical, message, properties, exception);
    }

    /// <summary>
    /// Logs the start of an operation and returns a disposable that logs completion.
    /// </summary>
    public static IOperationLogger BeginOperation(this ILogger logger, string operationName, object? properties = null)
    {
        return new OperationLogger(logger, operationName, properties);
    }

    /// <summary>
    /// Logs performance metrics for an operation.
    /// </summary>
    public static Task LogPerformanceAsync(this ILogger logger, string operationName, double durationMs,
        long? itemCount = null, long? bytesProcessed = null, object? properties = null)
    {
        var perfProperties = ConvertToProperties(properties);
        perfProperties["OperationName"] = operationName;
        perfProperties["DurationMs"] = durationMs;

        if (itemCount.HasValue)
            perfProperties["ItemCount"] = itemCount.Value;

        if (bytesProcessed.HasValue)
            perfProperties["BytesProcessed"] = bytesProcessed.Value;

        return logger.LogAsync(LogLevel.Information, $"Performance: {operationName}", perfProperties);
    }

    /// <summary>
    /// Logs a user action with context.
    /// </summary>
    public static Task LogUserActionAsync(this ILogger logger, string action, string userId, object? properties = null)
    {
        var actionProperties = ConvertToProperties(properties);
        actionProperties["Action"] = action;
        actionProperties["UserId"] = userId;
        actionProperties["EventType"] = "UserAction";

        return logger.LogAsync(LogLevel.Information, $"User action: {action}", actionProperties);
    }

    /// <summary>
    /// Logs a security event.
    /// </summary>
    public static Task LogSecurityEventAsync(this ILogger logger, string eventType, string? userId = null,
        bool success = true, object? properties = null)
    {
        var secProperties = ConvertToProperties(properties);
        secProperties["EventType"] = "Security";
        secProperties["SecurityEventType"] = eventType;
        secProperties["Success"] = success;

        if (!string.IsNullOrEmpty(userId))
            secProperties["UserId"] = userId;

        var level = success ? LogLevel.Information : LogLevel.Warning;
        return logger.LogAsync(level, $"Security event: {eventType}", secProperties);
    }

    /// <summary>
    /// Logs an audit event.
    /// </summary>
    public static Task LogAuditAsync(this ILogger logger, string action, string? userId = null,
        string? resource = null, bool success = true, object? properties = null)
    {
        var auditProperties = ConvertToProperties(properties);
        auditProperties["EventType"] = "Audit";
        auditProperties["Action"] = action;
        auditProperties["Success"] = success;

        if (!string.IsNullOrEmpty(userId))
            auditProperties["UserId"] = userId;

        if (!string.IsNullOrEmpty(resource))
            auditProperties["Resource"] = resource;

        var level = success ? LogLevel.Information : LogLevel.Error;
        return logger.LogAsync(level, $"Audit: {action}", auditProperties);
    }

    /// <summary>
    /// Logs configuration changes.
    /// </summary>
    public static Task LogConfigurationChangeAsync(this ILogger logger, string setting, object? oldValue,
        object? newValue, string? userId = null)
    {
        var configProperties = new Dictionary<string, object?>
        {
            ["EventType"] = "ConfigurationChange",
            ["Setting"] = setting,
            ["OldValue"] = oldValue,
            ["NewValue"] = newValue
        };

        if (!string.IsNullOrEmpty(userId))
            configProperties["UserId"] = userId;

        return logger.LogAsync(LogLevel.Information, $"Configuration changed: {setting}", configProperties);
    }

    /// <summary>
    /// Logs system metrics.
    /// </summary>
    public static Task LogSystemMetricsAsync(this ILogger logger, object metrics)
    {
        var metricsProperties = ConvertToProperties(metrics);
        metricsProperties["EventType"] = "SystemMetrics";

        return logger.LogAsync(LogLevel.Information, "System metrics", metricsProperties);
    }

    private static Task LogWithPropertiesAsync(ILogger logger, LogLevel level, string message,
        object? properties = null, Exception? exception = null)
    {
        if (properties == null)
        {
            return logger.LogAsync(level, message, exception);
        }

        var propertyDict = ConvertToProperties(properties);
        return logger.LogAsync(level, message, propertyDict, exception);
    }

    internal static Dictionary<string, object?> ConvertToProperties(object? obj)
    {
        if (obj == null)
            return new Dictionary<string, object?>();

        if (obj is Dictionary<string, object?> dict)
            return new Dictionary<string, object?>(dict);

        if (obj is IDictionary<string, object?> idict)
            return new Dictionary<string, object?>(idict);

        // Use reflection to convert anonymous objects or POCOs to dictionary
        var properties = new Dictionary<string, object?>();
        var type = obj.GetType();

        foreach (var prop in type.GetProperties())
        {
            if (prop.CanRead)
            {
                try
                {
                    var value = prop.GetValue(obj);
                    properties[prop.Name] = value;
                }
                catch
                {
                    // Skip properties that can't be read
                }
            }
        }

        return properties;
    }
}

/// <summary>
/// Interface for operation logging with automatic completion timing.
/// </summary>
public interface IOperationLogger : IDisposable
{
    /// <summary>
    /// Adds a property to the operation context.
    /// </summary>
    void AddProperty(string name, object? value);

    /// <summary>
    /// Marks the operation as failed.
    /// </summary>
    void SetFailed(Exception? exception = null);

    /// <summary>
    /// Gets the elapsed time since the operation started.
    /// </summary>
    TimeSpan Elapsed { get; }
}

/// <summary>
/// Implementation of operation logger that automatically logs start and completion.
/// </summary>
internal class OperationLogger : IOperationLogger
{
    private readonly ILogger _logger;
    private readonly string _operationName;
    private readonly Stopwatch _stopwatch;
    private readonly Dictionary<string, object?> _properties;
    private Exception? _exception;
    private bool _failed;
    private bool _disposed;

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public OperationLogger(ILogger logger, string operationName, object? initialProperties = null)
    {
        _logger = logger;
        _operationName = operationName;
        _stopwatch = Stopwatch.StartNew();
        _properties = StructuredLoggingExtensions.ConvertToProperties(initialProperties);

        // Log operation start
        var startProperties = new Dictionary<string, object?>(_properties)
        {
            ["OperationName"] = operationName,
            ["EventType"] = "OperationStart"
        };

        _logger.LogAsync(LogLevel.Debug, $"Started operation: {operationName}", startProperties);
    }

    public void AddProperty(string name, object? value)
    {
        _properties[name] = value;
    }

    public void SetFailed(Exception? exception = null)
    {
        _failed = true;
        _exception = exception;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _stopwatch.Stop();

        // Log operation completion
        var completionProperties = new Dictionary<string, object?>(_properties)
        {
            ["OperationName"] = _operationName,
            ["EventType"] = "OperationComplete",
            ["DurationMs"] = _stopwatch.Elapsed.TotalMilliseconds,
            ["Success"] = !_failed
        };

        var level = _failed ? LogLevel.Warning : LogLevel.Debug;
        var message = _failed
            ? $"Failed operation: {_operationName} ({_stopwatch.Elapsed.TotalMilliseconds:F2}ms)"
            : $"Completed operation: {_operationName} ({_stopwatch.Elapsed.TotalMilliseconds:F2}ms)";

        _logger.LogAsync(level, message, completionProperties, _exception);
    }
}

/// <summary>
/// Fluent builder for creating structured log entries.
/// </summary>
public class LogEntryBuilder
{
    private readonly ILogger _logger;
    private readonly LogLevel _level;
    private readonly string _message;
    private readonly Dictionary<string, object?> _properties = new();
    private Exception? _exception;

    internal LogEntryBuilder(ILogger logger, LogLevel level, string message)
    {
        _logger = logger;
        _level = level;
        _message = message;
    }

    /// <summary>
    /// Adds a property to the log entry.
    /// </summary>
    public LogEntryBuilder WithProperty(string name, object? value)
    {
        _properties[name] = value;
        return this;
    }

    /// <summary>
    /// Adds an exception to the log entry.
    /// </summary>
    public LogEntryBuilder WithException(Exception exception)
    {
        _exception = exception;
        return this;
    }

    /// <summary>
    /// Adds user context to the log entry.
    /// </summary>
    public LogEntryBuilder WithUser(string userId)
    {
        _properties["UserId"] = userId;
        return this;
    }

    /// <summary>
    /// Adds correlation ID to the log entry.
    /// </summary>
    public LogEntryBuilder WithCorrelation(string correlationId)
    {
        _properties["CorrelationId"] = correlationId;
        return this;
    }

    /// <summary>
    /// Writes the log entry.
    /// </summary>
    public Task WriteAsync()
    {
        return _logger.LogAsync(_level, _message, _properties, _exception);
    }
}

/// <summary>
/// Additional extension methods for fluent logging.
/// </summary>
public static class FluentLoggingExtensions
{
    /// <summary>
    /// Creates a fluent log entry builder.
    /// </summary>
    public static LogEntryBuilder Log(this ILogger logger, LogLevel level, string message)
    {
        return new LogEntryBuilder(logger, level, message);
    }

    /// <summary>
    /// Creates a fluent information log entry builder.
    /// </summary>
    public static LogEntryBuilder LogInformation(this ILogger logger, string message)
    {
        return new LogEntryBuilder(logger, LogLevel.Information, message);
    }

    /// <summary>
    /// Creates a fluent warning log entry builder.
    /// </summary>
    public static LogEntryBuilder LogWarning(this ILogger logger, string message)
    {
        return new LogEntryBuilder(logger, LogLevel.Warning, message);
    }

    /// <summary>
    /// Creates a fluent error log entry builder.
    /// </summary>
    public static LogEntryBuilder LogError(this ILogger logger, string message)
    {
        return new LogEntryBuilder(logger, LogLevel.Error, message);
    }
}