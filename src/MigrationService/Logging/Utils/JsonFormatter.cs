using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Utils;

/// <summary>
/// JSON log formatter that outputs structured log entries.
/// </summary>
public class JsonFormatter : ILogFormatter
{
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// Initializes a new instance of the JsonFormatter.
    /// </summary>
    /// <param name="indented">Whether to format JSON with indentation.</param>
    public JsonFormatter(bool indented = false)
    {
        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
    }

    public string Format(LogEntry entry)
    {
        var logObject = new
        {
            id = entry.Id,
            timestamp = entry.Timestamp,
            level = entry.Level.ToString(),
            category = entry.Category,
            message = entry.Message,
            machineName = entry.MachineName,
            processId = entry.ProcessId,
            threadId = entry.ThreadId,
            userId = entry.UserId,
            correlationId = entry.CorrelationId,
            properties = entry.Properties.Count > 0 ? entry.Properties : null,
            performance = entry.Performance != null ? new
            {
                durationMs = entry.Performance.DurationMs,
                memoryBytes = entry.Performance.MemoryBytes,
                cpuPercent = entry.Performance.CpuPercent,
                itemCount = entry.Performance.ItemCount,
                customMetrics = entry.Performance.CustomMetrics.Count > 0 ? entry.Performance.CustomMetrics : null
            } : null,
            exception = entry.Exception != null ? FormatException(entry.Exception) : null
        };

        return JsonSerializer.Serialize(logObject, _serializerOptions);
    }

    private object FormatException(Exception exception)
    {
        var exceptions = new List<object>();
        var current = exception;

        while (current != null)
        {
            exceptions.Add(new
            {
                type = current.GetType().FullName,
                message = current.Message,
                source = current.Source,
                stackTrace = current.StackTrace?.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                data = current.Data.Count > 0 ? current.Data : null
            });

            current = current.InnerException;
        }

        return new
        {
            type = exception.GetType().FullName,
            message = exception.Message,
            source = exception.Source,
            stackTrace = exception.StackTrace?.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            data = exception.Data.Count > 0 ? exception.Data : null,
            innerExceptions = exceptions.Count > 1 ? exceptions.GetRange(1, exceptions.Count - 1) : null
        };
    }
}