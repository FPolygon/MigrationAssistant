using System;
using System.Collections.Generic;
using System.Threading;

namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Provides ambient context for logging that flows with async operations.
/// </summary>
public static class LogContext
{
    internal static readonly AsyncLocal<LogContextScope?> _currentScope = new();
    
    /// <summary>
    /// Gets the current log context scope.
    /// </summary>
    public static LogContextScope? Current => _currentScope.Value;
    
    /// <summary>
    /// Pushes a new property onto the context stack.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value.</param>
    /// <returns>A disposable scope that removes the property when disposed.</returns>
    public static IDisposable PushProperty(string name, object? value)
    {
        return new LogContextScope(name, value);
    }
    
    /// <summary>
    /// Pushes multiple properties onto the context stack.
    /// </summary>
    /// <param name="properties">The properties to push.</param>
    /// <returns>A disposable scope that removes the properties when disposed.</returns>
    public static IDisposable PushProperties(IDictionary<string, object?> properties)
    {
        return new LogContextScope(properties);
    }
    
    /// <summary>
    /// Sets the correlation ID for the current context.
    /// </summary>
    /// <param name="correlationId">The correlation ID.</param>
    /// <returns>A disposable scope that removes the correlation ID when disposed.</returns>
    public static IDisposable PushCorrelationId(string correlationId)
    {
        return PushProperty("CorrelationId", correlationId);
    }
    
    /// <summary>
    /// Sets the user context for logging.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="sessionId">Optional session ID.</param>
    /// <returns>A disposable scope that removes the user context when disposed.</returns>
    public static IDisposable PushUserContext(string userId, string? sessionId = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["UserId"] = userId,
            ["SessionId"] = sessionId
        };
        return PushProperties(properties);
    }
    
    /// <summary>
    /// Gets all properties from the current context.
    /// </summary>
    /// <returns>A dictionary of context properties.</returns>
    public static IDictionary<string, object?> GetProperties()
    {
        var properties = new Dictionary<string, object?>();
        var scope = _currentScope.Value;
        
        while (scope != null)
        {
            foreach (var (key, value) in scope.Properties)
            {
                // Don't overwrite properties set in inner scopes
                properties.TryAdd(key, value);
            }
            scope = scope.Parent;
        }
        
        return properties;
    }
}

/// <summary>
/// Represents a scope of log context properties.
/// </summary>
public sealed class LogContextScope : IDisposable
{
    private readonly LogContextScope? _parent;
    private bool _disposed;
    
    /// <summary>
    /// Gets the properties in this scope.
    /// </summary>
    public IDictionary<string, object?> Properties { get; }
    
    /// <summary>
    /// Gets the parent scope.
    /// </summary>
    public LogContextScope? Parent => _parent;
    
    internal LogContextScope(string name, object? value)
    {
        _parent = LogContext.Current;
        Properties = new Dictionary<string, object?> { [name] = value };
        LogContext._currentScope.Value = this;
    }
    
    internal LogContextScope(IDictionary<string, object?> properties)
    {
        _parent = LogContext.Current;
        Properties = new Dictionary<string, object?>(properties);
        LogContext._currentScope.Value = this;
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        LogContext._currentScope.Value = _parent;
    }
}