using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.EventLog;

namespace MigrationTool.Service.Logging.Providers;

/// <summary>
/// Logging provider that writes log entries to the Windows Event Log.
/// </summary>
public class EventLogProvider : ILoggingProvider
{
    private LoggingSettings _settings = new();
    private EventLogSettings _eventLogSettings = new();
    private System.Diagnostics.EventLog? _eventLog;
    private bool _disposed;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public string Name => "EventLogProvider";

    public bool IsEnabled => _settings.Enabled && _eventLog != null;

    /// <summary>
    /// Initializes a new instance of the EventLogProvider.
    /// </summary>
    public EventLogProvider()
    {
    }

    public void Configure(LoggingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // Extract event log specific settings
        _eventLogSettings = new EventLogSettings();

        if (settings.ProviderSettings.TryGetValue("Source", out var source))
        {
            _eventLogSettings.Source = source?.ToString() ?? "MigrationTool";
        }

        if (settings.ProviderSettings.TryGetValue("LogName", out var logName))
        {
            _eventLogSettings.LogName = logName?.ToString() ?? "Application";
        }

        if (settings.ProviderSettings.TryGetValue("MachineName", out var machineName))
        {
            _eventLogSettings.MachineName = machineName?.ToString() ?? ".";
        }

        if (settings.ProviderSettings.TryGetValue("MaxMessageLength", out var maxLength) &&
            maxLength is int lengthInt)
        {
            _eventLogSettings.MaxMessageLength = lengthInt;
        }

        // Initialize event log
        InitializeEventLog();
    }

    public bool IsLevelEnabled(LogLevel level)
    {
        // Only log Information level and above to Event Log to avoid spam
        return level >= LogLevel.Information && level.IsEnabled(_settings.MinimumLevel);
    }

    public async Task WriteLogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !IsLevelEnabled(entry.Level) || _disposed)
            return;

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await WriteToEventLogAsync(entry, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Event log writes are synchronous, no flushing needed
        return Task.CompletedTask;
    }

    private void InitializeEventLog()
    {
        try
        {
            // Check if source exists, create if it doesn't
            if (!System.Diagnostics.EventLog.SourceExists(_eventLogSettings.Source))
            {
                // Note: Creating an event source requires administrative privileges
                // This should be done during service installation
                try
                {
                    System.Diagnostics.EventLog.CreateEventSource(
                        _eventLogSettings.Source,
                        _eventLogSettings.LogName);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to create event source '{_eventLogSettings.Source}': {ex.Message}");
                    Console.Error.WriteLine("Event logging will be disabled. Run with administrator privileges to create the event source.");
                    return;
                }
            }

            _eventLog = new System.Diagnostics.EventLog(_eventLogSettings.LogName, _eventLogSettings.MachineName, _eventLogSettings.Source);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize event log: {ex.Message}");
        }
    }

    private async Task WriteToEventLogAsync(LogEntry entry, CancellationToken cancellationToken)
    {
        if (_eventLog == null) return;

        try
        {
            var eventId = EventIdMapper.GetEventId(entry.Level, entry.Category);
            var eventType = EventIdMapper.GetEventType(entry.Level);
            var message = FormatMessage(entry);

            // Truncate message if too long
            if (message.Length > _eventLogSettings.MaxMessageLength)
            {
                message = message.Substring(0, _eventLogSettings.MaxMessageLength - 3) + "...";
            }

            await Task.Run(() =>
            {
                _eventLog.WriteEntry(message, eventType, eventId);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Don't let event log errors break the application
            Console.Error.WriteLine($"Failed to write to event log: {ex.Message}");
        }
    }

    private string FormatMessage(LogEntry entry)
    {
        var sb = new StringBuilder();

        // Main message
        sb.AppendLine(entry.Message);

        // Category
        if (!string.IsNullOrEmpty(entry.Category))
        {
            sb.AppendLine($"Category: {entry.Category}");
        }

        // User context
        if (!string.IsNullOrEmpty(entry.UserId))
        {
            sb.AppendLine($"User: {entry.UserId}");
        }

        // Correlation ID
        if (!string.IsNullOrEmpty(entry.CorrelationId))
        {
            sb.AppendLine($"Correlation ID: {entry.CorrelationId}");
        }

        // Machine and process info
        sb.AppendLine($"Machine: {entry.MachineName}");
        sb.AppendLine($"Process: {entry.ProcessId}");
        sb.AppendLine($"Thread: {entry.ThreadId}");

        // Performance metrics
        if (entry.Performance != null)
        {
            sb.AppendLine($"Duration: {entry.Performance.DurationMs:F2}ms");

            if (entry.Performance.ItemCount.HasValue)
            {
                sb.AppendLine($"Items: {entry.Performance.ItemCount}");
            }

            if (entry.Performance.MemoryBytes.HasValue)
            {
                sb.AppendLine($"Memory: {FormatBytes(entry.Performance.MemoryBytes.Value)}");
            }
        }

        // Exception details
        if (entry.Exception != null)
        {
            sb.AppendLine();
            sb.AppendLine("Exception Details:");
            sb.AppendLine($"Type: {entry.Exception.GetType().FullName}");
            sb.AppendLine($"Message: {entry.Exception.Message}");

            if (!string.IsNullOrEmpty(entry.Exception.StackTrace))
            {
                sb.AppendLine("Stack Trace:");
                sb.AppendLine(entry.Exception.StackTrace);
            }

            // Inner exception
            var innerEx = entry.Exception.InnerException;
            if (innerEx != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Inner Exception: {innerEx.GetType().FullName}");
                sb.AppendLine($"Inner Message: {innerEx.Message}");
            }
        }

        // Important properties
        if (entry.Properties.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Properties:");

            var propertyCount = 0;
            foreach (var (key, value) in entry.Properties)
            {
                if (propertyCount >= 10) // Limit properties to keep message reasonable
                {
                    sb.AppendLine("... (additional properties truncated)");
                    break;
                }

                sb.AppendLine($"  {key}: {value}");
                propertyCount++;
            }
        }

        return sb.ToString().TrimEnd();
    }

    private string FormatBytes(long bytes)
    {
        const int unit = 1024;
        if (bytes < unit) return $"{bytes} B";

        int exp = (int)(Math.Log(bytes) / Math.Log(unit));
        string pre = "KMGTPE"[exp - 1].ToString();

        return $"{bytes / Math.Pow(unit, exp):F1} {pre}B";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            _eventLog?.Dispose();
        }
        catch { }

        _writeLock.Dispose();
    }
}

/// <summary>
/// Settings specific to Windows Event Log logging.
/// </summary>
public class EventLogSettings
{
    /// <summary>
    /// The event source name for the application.
    /// </summary>
    public string Source { get; set; } = "MigrationTool";

    /// <summary>
    /// The event log name (usually "Application").
    /// </summary>
    public string LogName { get; set; } = "Application";

    /// <summary>
    /// The machine name for the event log (usually ".").
    /// </summary>
    public string MachineName { get; set; } = ".";

    /// <summary>
    /// Maximum length of a single event message.
    /// </summary>
    public int MaxMessageLength { get; set; } = 31839; // Windows Event Log limit
}