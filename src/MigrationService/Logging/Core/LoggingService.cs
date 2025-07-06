using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Core;

/// <summary>
/// Central logging service that manages multiple logging providers.
/// </summary>
public class LoggingService : IDisposable
{
    private readonly ConcurrentDictionary<string, ILoggingProvider> _providers = new();
    private readonly SemaphoreSlim _configLock = new(1, 1);
    private LoggingSettings _settings = new();
    private bool _disposed;
    
    /// <summary>
    /// Gets the singleton instance of the logging service.
    /// </summary>
    public static LoggingService Instance { get; } = new();
    
    /// <summary>
    /// Gets the current logging settings.
    /// </summary>
    public LoggingSettings Settings => _settings;
    
    /// <summary>
    /// Gets the collection of registered providers.
    /// </summary>
    public IReadOnlyDictionary<string, ILoggingProvider> Providers => _providers;
    
    /// <summary>
    /// Registers a logging provider.
    /// </summary>
    /// <param name="provider">The provider to register.</param>
    /// <exception cref="ArgumentNullException">If provider is null.</exception>
    /// <exception cref="InvalidOperationException">If a provider with the same name already exists.</exception>
    public void RegisterProvider(ILoggingProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        
        if (!_providers.TryAdd(provider.Name, provider))
        {
            throw new InvalidOperationException($"A provider with name '{provider.Name}' is already registered.");
        }
        
        provider.Configure(_settings);
    }
    
    /// <summary>
    /// Unregisters a logging provider.
    /// </summary>
    /// <param name="providerName">The name of the provider to unregister.</param>
    /// <returns>True if the provider was unregistered; otherwise, false.</returns>
    public bool UnregisterProvider(string providerName)
    {
        if (_providers.TryRemove(providerName, out var provider))
        {
            provider.Dispose();
            return true;
        }
        return false;
    }
    
    /// <summary>
    /// Configures the logging service with new settings.
    /// </summary>
    /// <param name="settings">The new settings.</param>
    public async Task ConfigureAsync(LoggingSettings settings)
    {
        await _configLock.WaitAsync();
        try
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            
            // Reconfigure all providers
            foreach (var provider in _providers.Values)
            {
                provider.Configure(_settings);
            }
        }
        finally
        {
            _configLock.Release();
        }
    }
    
    /// <summary>
    /// Logs a message with the specified level and category.
    /// </summary>
    public async Task LogAsync(LogLevel level, string category, string message, 
        Exception? exception = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        var entry = CreateLogEntry(level, category, message, exception);
        await WriteToProvidersAsync(entry, cancellationToken);
    }
    
    /// <summary>
    /// Logs a structured message with additional properties.
    /// </summary>
    public async Task LogAsync(LogLevel level, string category, string message,
        IDictionary<string, object?> properties, Exception? exception = null, 
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        var entry = CreateLogEntry(level, category, message, exception);
        foreach (var (key, value) in properties)
        {
            entry.Properties[key] = value;
        }
        
        await WriteToProvidersAsync(entry, cancellationToken);
    }
    
    /// <summary>
    /// Logs a performance metric.
    /// </summary>
    public async Task LogPerformanceAsync(string category, string operation, double durationMs,
        IDictionary<string, double>? customMetrics = null, CancellationToken cancellationToken = default)
    {
        if (_disposed) return;
        
        var entry = CreateLogEntry(LogLevel.Information, category, $"Performance: {operation}", null);
        entry.Performance = new PerformanceMetrics
        {
            DurationMs = durationMs,
            CustomMetrics = customMetrics != null ? new Dictionary<string, double>(customMetrics) : new Dictionary<string, double>()
        };
        
        await WriteToProvidersAsync(entry, cancellationToken);
    }
    
    /// <summary>
    /// Creates a logger instance for a specific category.
    /// </summary>
    /// <param name="category">The category name.</param>
    /// <returns>A logger instance.</returns>
    public ILogger CreateLogger(string category)
    {
        return new Logger(this, category);
    }
    
    /// <summary>
    /// Creates a logger instance for a specific type.
    /// </summary>
    /// <typeparam name="T">The type to create a logger for.</typeparam>
    /// <returns>A logger instance.</returns>
    public ILogger<T> CreateLogger<T>()
    {
        return new Logger<T>(this);
    }
    
    /// <summary>
    /// Flushes all providers.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _providers.Values
            .Where(p => p.IsEnabled)
            .Select(p => p.FlushAsync(cancellationToken));
        
        await Task.WhenAll(tasks);
    }
    
    private LogEntry CreateLogEntry(LogLevel level, string category, string message, Exception? exception)
    {
        var entry = new LogEntry
        {
            Level = level,
            Category = category,
            Message = message,
            Exception = exception
        };
        
        // Add context properties
        var contextProps = LogContext.GetProperties();
        foreach (var (key, value) in contextProps)
        {
            entry.Properties[key] = value;
        }
        
        // Set special properties from context
        if (contextProps.TryGetValue("CorrelationId", out var correlationId))
        {
            entry.CorrelationId = correlationId?.ToString();
        }
        
        if (contextProps.TryGetValue("UserId", out var userId))
        {
            entry.UserId = userId?.ToString();
        }
        
        return entry;
    }
    
    private async Task WriteToProvidersAsync(LogEntry entry, CancellationToken cancellationToken)
    {
        var effectiveLevel = _settings.GetEffectiveLevel(entry.Category);
        if (!entry.Level.IsEnabled(effectiveLevel))
            return;
        
        var tasks = new List<Task>();
        
        foreach (var provider in _providers.Values)
        {
            if (provider.IsEnabled && provider.IsLevelEnabled(entry.Level))
            {
                tasks.Add(WriteToProviderSafeAsync(provider, entry, cancellationToken));
            }
        }
        
        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
        }
    }
    
    private async Task WriteToProviderSafeAsync(ILoggingProvider provider, LogEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await provider.WriteLogAsync(entry, cancellationToken);
        }
        catch (Exception ex)
        {
            // Log to console as fallback
            Console.Error.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Failed to write to provider '{provider.Name}': {ex.Message}");
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        // Flush all providers
        try
        {
            FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch { }
        
        // Dispose all providers
        foreach (var provider in _providers.Values)
        {
            try
            {
                provider.Dispose();
            }
            catch { }
        }
        
        _providers.Clear();
        _configLock.Dispose();
    }
}

/// <summary>
/// Logger interface for category-specific logging.
/// </summary>
public interface ILogger
{
    string Category { get; }
    Task LogAsync(LogLevel level, string message, Exception? exception = null);
    Task LogAsync(LogLevel level, string message, IDictionary<string, object?> properties, Exception? exception = null);
}

/// <summary>
/// Generic logger interface.
/// </summary>
public interface ILogger<T> : ILogger
{
}

/// <summary>
/// Implementation of ILogger for a specific category.
/// </summary>
internal class Logger : ILogger
{
    private readonly LoggingService _service;
    
    public string Category { get; }
    
    public Logger(LoggingService service, string category)
    {
        _service = service;
        Category = category;
    }
    
    public Task LogAsync(LogLevel level, string message, Exception? exception = null)
    {
        return _service.LogAsync(level, Category, message, exception);
    }
    
    public Task LogAsync(LogLevel level, string message, IDictionary<string, object?> properties, Exception? exception = null)
    {
        return _service.LogAsync(level, Category, message, properties, exception);
    }
}

/// <summary>
/// Generic implementation of ILogger.
/// </summary>
internal class Logger<T> : Logger, ILogger<T>
{
    public Logger(LoggingService service) : base(service, typeof(T).FullName ?? typeof(T).Name)
    {
    }
}