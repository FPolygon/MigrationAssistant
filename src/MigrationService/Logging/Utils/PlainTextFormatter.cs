using System;
using System.Globalization;
using System.Linq;
using System.Text;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Utils;

/// <summary>
/// Plain text log formatter that outputs human-readable log entries.
/// </summary>
public class PlainTextFormatter : ILogFormatter
{
    private readonly bool _useUtc;
    private readonly bool _includeCategory;
    private readonly bool _includeThreadId;
    private readonly bool _includeProperties;

    /// <summary>
    /// Initializes a new instance of the PlainTextFormatter.
    /// </summary>
    /// <param name="useUtc">Whether to use UTC timestamps.</param>
    /// <param name="includeCategory">Whether to include category in output.</param>
    /// <param name="includeThreadId">Whether to include thread ID in output.</param>
    /// <param name="includeProperties">Whether to include properties in output.</param>
    public PlainTextFormatter(bool useUtc = true, bool includeCategory = true,
        bool includeThreadId = true, bool includeProperties = true)
    {
        _useUtc = useUtc;
        _includeCategory = includeCategory;
        _includeThreadId = includeThreadId;
        _includeProperties = includeProperties;
    }

    public string Format(LogEntry entry)
    {
        var sb = new StringBuilder();

        // Timestamp
        var timestamp = _useUtc ? entry.Timestamp : entry.Timestamp.ToLocalTime();
        sb.Append(timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        if (_useUtc) sb.Append(" UTC");

        // Level
        sb.Append($" [{entry.Level.ToShortString()}]");

        // Thread ID
        if (_includeThreadId)
        {
            sb.Append($" [{entry.ThreadId:D4}]");
        }

        // Category
        if (_includeCategory && !string.IsNullOrEmpty(entry.Category))
        {
            sb.Append($" {entry.Category}:");
        }

        // Message
        sb.Append($" {entry.Message}");

        // User context
        if (!string.IsNullOrEmpty(entry.UserId))
        {
            sb.Append($" [User: {entry.UserId}]");
        }

        // Correlation ID
        if (!string.IsNullOrEmpty(entry.CorrelationId))
        {
            sb.Append($" [Correlation: {entry.CorrelationId}]");
        }

        // Performance metrics
        if (entry.Performance != null)
        {
            sb.Append($" [Duration: {entry.Performance.DurationMs:F2}ms");

            if (entry.Performance.ItemCount.HasValue)
            {
                sb.Append($", Items: {entry.Performance.ItemCount}");
            }

            if (entry.Performance.MemoryBytes.HasValue)
            {
                sb.Append($", Memory: {FormatBytes(entry.Performance.MemoryBytes.Value)}");
            }

            if (entry.Performance.CpuPercent.HasValue)
            {
                sb.Append($", CPU: {entry.Performance.CpuPercent:F1}%");
            }

            sb.Append("]");
        }

        // Properties
        if (_includeProperties && entry.Properties.Count > 0)
        {
            var props = entry.Properties
                .Where(p => !IsInternalProperty(p.Key))
                .Select(p => $"{p.Key}={p.Value}")
                .Take(10); // Limit to prevent excessive output

            if (props.Any())
            {
                sb.Append($" [{string.Join(", ", props)}]");
            }
        }

        // Exception
        if (entry.Exception != null)
        {
            sb.AppendLine();
            sb.Append(FormatException(entry.Exception));
        }

        return sb.ToString();
    }

    private string FormatException(Exception exception)
    {
        var sb = new StringBuilder();
        var indent = "  ";

        sb.AppendLine($"{indent}Exception: {exception.GetType().Name}");
        sb.AppendLine($"{indent}Message: {exception.Message}");

        if (!string.IsNullOrEmpty(exception.StackTrace))
        {
            sb.AppendLine($"{indent}Stack Trace:");
            var stackLines = exception.StackTrace.Split('\n');
            foreach (var line in stackLines)
            {
                sb.AppendLine($"{indent}  {line.Trim()}");
            }
        }

        // Inner exceptions
        var innerEx = exception.InnerException;
        var innerIndent = indent + "  ";

        while (innerEx != null)
        {
            sb.AppendLine($"{innerIndent}Inner Exception: {innerEx.GetType().Name}");
            sb.AppendLine($"{innerIndent}Message: {innerEx.Message}");

            if (!string.IsNullOrEmpty(innerEx.StackTrace))
            {
                sb.AppendLine($"{innerIndent}Stack Trace:");
                var innerStackLines = innerEx.StackTrace.Split('\n');
                foreach (var line in innerStackLines)
                {
                    sb.AppendLine($"{innerIndent}  {line.Trim()}");
                }
            }

            innerEx = innerEx.InnerException;
            innerIndent += "  ";
        }

        return sb.ToString().TrimEnd();
    }

    private bool IsInternalProperty(string key)
    {
        return key switch
        {
            "CorrelationId" => true,
            "UserId" => true,
            "SessionId" => true,
            _ => false
        };
    }

    private string FormatBytes(long bytes)
    {
        const int unit = 1024;
        if (bytes < unit) return $"{bytes} B";

        int exp = (int)(Math.Log(bytes) / Math.Log(unit));
        string pre = "KMGTPE"[exp - 1].ToString();

        return $"{bytes / Math.Pow(unit, exp):F1} {pre}B";
    }
}