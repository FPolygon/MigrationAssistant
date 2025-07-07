using System;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Utils;

namespace MigrationTool.Service.Logging.Providers;

/// <summary>
/// A logging provider that wraps another provider with asynchronous buffering capabilities.
/// </summary>
public class BufferedLogProvider : ILoggingProvider
{
    private readonly ILoggingProvider _innerProvider;
    private readonly AsyncLogWriter _asyncWriter;
    private bool _disposed;

    /// <summary>
    /// Gets the name of the buffered provider.
    /// </summary>
    public string Name => $"Buffered({_innerProvider.Name})";

    /// <summary>
    /// Gets whether the provider is enabled.
    /// </summary>
    public bool IsEnabled => _innerProvider.IsEnabled && !_disposed;

    /// <summary>
    /// Gets the underlying provider.
    /// </summary>
    public ILoggingProvider InnerProvider => _innerProvider;

    /// <summary>
    /// Gets the async writer statistics.
    /// </summary>
    public AsyncLogWriterStatistics Statistics => _asyncWriter.GetStatistics();

    /// <summary>
    /// Event raised when the buffer queue experiences pressure.
    /// </summary>
    public event EventHandler<QueuePressureEventArgs>? QueuePressure
    {
        add => _asyncWriter.QueuePressure += value;
        remove => _asyncWriter.QueuePressure -= value;
    }

    /// <summary>
    /// Initializes a new instance of the BufferedLogProvider.
    /// </summary>
    /// <param name="innerProvider">The provider to wrap with buffering.</param>
    /// <param name="options">Options for the async writer.</param>
    public BufferedLogProvider(ILoggingProvider innerProvider, AsyncLogWriterOptions? options = null)
    {
        _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
        _asyncWriter = new AsyncLogWriter(_innerProvider, options);
    }

    /// <summary>
    /// Configures the buffered provider.
    /// </summary>
    /// <param name="settings">The logging settings.</param>
    public void Configure(LoggingSettings settings)
    {
        _innerProvider.Configure(settings);
    }

    /// <summary>
    /// Checks if the specified log level is enabled.
    /// </summary>
    /// <param name="level">The log level to check.</param>
    /// <returns>True if the level is enabled; otherwise, false.</returns>
    public bool IsLevelEnabled(LogLevel level)
    {
        return _innerProvider.IsLevelEnabled(level);
    }

    /// <summary>
    /// Writes a log entry asynchronously by queuing it for background processing.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    /// <param name="cancellationToken">Cancellation token (not used for queuing).</param>
    /// <returns>A completed task (queuing is synchronous).</returns>
    public Task WriteLogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;

        // Queue the entry for asynchronous processing
        var queued = _asyncWriter.QueueLogEntry(entry);

        if (!queued)
        {
            // If queueing failed, we could optionally fall back to synchronous writing
            // For now, we'll just complete the task
            Console.Error.WriteLine($"Failed to queue log entry for provider '{Name}'");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Flushes all buffered log entries.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous flush operation.</returns>
    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return;

        await _asyncWriter.FlushAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Creates a buffered version of an existing provider.
    /// </summary>
    /// <param name="provider">The provider to wrap.</param>
    /// <param name="options">Buffering options.</param>
    /// <returns>A buffered version of the provider.</returns>
    public static BufferedLogProvider Wrap(ILoggingProvider provider, AsyncLogWriterOptions? options = null)
    {
        if (provider is BufferedLogProvider buffered)
        {
            // Already buffered, return as-is
            return buffered;
        }

        return new BufferedLogProvider(provider, options);
    }

    /// <summary>
    /// Creates buffering options optimized for high-throughput scenarios.
    /// </summary>
    /// <returns>Optimized async writer options.</returns>
    public static AsyncLogWriterOptions CreateHighThroughputOptions()
    {
        return new AsyncLogWriterOptions
        {
            MaxQueueSize = 50000,
            HighWatermark = 37500,
            BatchSize = 500,
            FlushInterval = TimeSpan.FromSeconds(2),
            OverflowPolicy = OverflowPolicy.DropOldest
        };
    }

    /// <summary>
    /// Creates buffering options optimized for low-latency scenarios.
    /// </summary>
    /// <returns>Optimized async writer options.</returns>
    public static AsyncLogWriterOptions CreateLowLatencyOptions()
    {
        return new AsyncLogWriterOptions
        {
            MaxQueueSize = 5000,
            HighWatermark = 3750,
            BatchSize = 50,
            FlushInterval = TimeSpan.FromMilliseconds(500),
            OverflowPolicy = OverflowPolicy.Block
        };
    }

    /// <summary>
    /// Creates buffering options optimized for memory-constrained scenarios.
    /// </summary>
    /// <returns>Optimized async writer options.</returns>
    public static AsyncLogWriterOptions CreateMemoryOptimizedOptions()
    {
        return new AsyncLogWriterOptions
        {
            MaxQueueSize = 1000,
            HighWatermark = 750,
            BatchSize = 25,
            FlushInterval = TimeSpan.FromSeconds(10),
            OverflowPolicy = OverflowPolicy.DropOldest
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            // Flush and dispose the async writer
            _asyncWriter.Dispose();

            // Dispose the inner provider
            _innerProvider.Dispose();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error disposing buffered log provider: {ex.Message}");
        }
    }
}