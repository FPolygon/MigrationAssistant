using System;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Utils;

namespace MigrationTool.Service.Logging.Providers;

/// <summary>
/// Logging provider that writes log entries to the console with optional color coding.
/// </summary>
public class ConsoleLogProvider : ILoggingProvider
{
    private LoggingSettings _settings = new();
    private ConsoleLogSettings _consoleSettings = new();
    private ILogFormatter _formatter;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;
    
    public string Name => "ConsoleLogger";
    
    public bool IsEnabled => _settings.Enabled && !_disposed;
    
    /// <summary>
    /// Initializes a new instance of the ConsoleLogProvider.
    /// </summary>
    /// <param name="formatter">The log formatter to use. If null, uses PlainTextFormatter.</param>
    public ConsoleLogProvider(ILogFormatter? formatter = null)
    {
        _formatter = formatter ?? new PlainTextFormatter(useUtc: false, includeThreadId: false);
    }
    
    public void Configure(LoggingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        
        // Extract console-specific settings
        _consoleSettings = new ConsoleLogSettings();
        
        if (settings.ProviderSettings.TryGetValue("UseColors", out var useColors) && 
            useColors is bool colorsBool)
        {
            _consoleSettings.UseColors = colorsBool;
        }
        
        if (settings.ProviderSettings.TryGetValue("IncludeTimestamp", out var includeTs) && 
            includeTs is bool tsBool)
        {
            _consoleSettings.IncludeTimestamp = tsBool;
        }
        
        if (settings.ProviderSettings.TryGetValue("IncludeCategory", out var includeCat) && 
            includeCat is bool catBool)
        {
            _consoleSettings.IncludeCategory = catBool;
        }
        
        // Update formatter based on settings
        _formatter = new PlainTextFormatter(
            useUtc: false, 
            includeCategory: _consoleSettings.IncludeCategory, 
            includeThreadId: false,
            includeProperties: false);
    }
    
    public bool IsLevelEnabled(LogLevel level)
    {
        return level.IsEnabled(_settings.MinimumLevel);
    }
    
    public async Task WriteLogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || !IsLevelEnabled(entry.Level) || _disposed)
            return;
        
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var message = _formatter.Format(entry);
            
            if (_consoleSettings.UseColors && Console.IsOutputRedirected == false)
            {
                WriteColoredMessage(entry.Level, message);
            }
            else
            {
                Console.WriteLine(message);
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }
    
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        // Console output is automatically flushed
        return Task.CompletedTask;
    }
    
    private void WriteColoredMessage(LogLevel level, string message)
    {
        var originalColor = Console.ForegroundColor;
        
        try
        {
            Console.ForegroundColor = GetLogLevelColor(level);
            Console.WriteLine(message);
        }
        finally
        {
            Console.ForegroundColor = originalColor;
        }
    }
    
    private ConsoleColor GetLogLevelColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Critical => ConsoleColor.Magenta,
            LogLevel.Error => ConsoleColor.Red,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Information => ConsoleColor.White,
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Verbose => ConsoleColor.DarkGray,
            _ => ConsoleColor.White
        };
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _writeLock.Dispose();
    }
}

/// <summary>
/// Settings specific to console logging.
/// </summary>
public class ConsoleLogSettings
{
    /// <summary>
    /// Whether to use colored output for different log levels.
    /// </summary>
    public bool UseColors { get; set; } = true;
    
    /// <summary>
    /// Whether to include timestamps in console output.
    /// </summary>
    public bool IncludeTimestamp { get; set; } = true;
    
    /// <summary>
    /// Whether to include category information in console output.
    /// </summary>
    public bool IncludeCategory { get; set; } = true;
}