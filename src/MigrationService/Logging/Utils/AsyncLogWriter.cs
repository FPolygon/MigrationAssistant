using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Utils;

/// <summary>
/// Provides asynchronous, non-blocking log writing with buffering and batching capabilities.
/// </summary>
public class AsyncLogWriter : IDisposable
{
    private readonly ILoggingProvider _provider;
    private readonly AsyncLogWriterOptions _options;
    private readonly ConcurrentQueue<LogEntry> _logQueue = new();
    private readonly SemaphoreSlim _processingSignal = new(0);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _processingTask;

    private volatile bool _disposed;
    private long _queueSize;

    /// <summary>
    /// Gets the current number of buffered log entries.
    /// </summary>
    public int QueueSize => (int)_queueSize;

    /// <summary>
    /// Gets whether the writer is running.
    /// </summary>
    public bool IsRunning => !_disposed && !_processingTask.IsCompleted;

    /// <summary>
    /// Event raised when the queue reaches high watermark.
    /// </summary>
    public event EventHandler<QueuePressureEventArgs>? QueuePressure;

    /// <summary>
    /// Initializes a new instance of the AsyncLogWriter.
    /// </summary>
    /// <param name="provider">The logging provider to write to.</param>
    /// <param name="options">Options for the async writer.</param>
    public AsyncLogWriter(ILoggingProvider provider, AsyncLogWriterOptions? options = null)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? new AsyncLogWriterOptions();

        _processingTask = Task.Run(ProcessLogEntriesAsync);
    }

    /// <summary>
    /// Queues a log entry for asynchronous writing.
    /// </summary>
    /// <param name="entry">The log entry to write.</param>
    /// <returns>True if the entry was queued; false if the queue is full or the writer is disposed.</returns>
    public bool QueueLogEntry(LogEntry entry)
    {
        if (_disposed) return false;

        // For DropNewest policy, we need to check size before attempting to add
        if (_options.OverflowPolicy == OverflowPolicy.DropNewest)
        {
            var currentSize = Interlocked.Read(ref _queueSize);
            if (currentSize >= _options.MaxQueueSize)
            {
                OnQueuePressure(new QueuePressureEventArgs
                {
                    CurrentSize = (int)currentSize,
                    MaxSize = _options.MaxQueueSize,
                    DroppedEntry = entry
                });
                return false;
            }
        }

        // Check current size for DropOldest policy
        if (_options.OverflowPolicy == OverflowPolicy.DropOldest)
        {
            var currentSize = Interlocked.Read(ref _queueSize);
            if (currentSize >= _options.MaxQueueSize)
            {
                // Try to remove an old entry first
                if (_logQueue.TryDequeue(out var _))
                {
                    Interlocked.Decrement(ref _queueSize);
                }
            }
        }

        // Increment the queue size first to reserve our spot
        var newSize = Interlocked.Increment(ref _queueSize);
        
        // Then enqueue the entry
        _logQueue.Enqueue(entry);

        // Signal the processing task
        _processingSignal.Release();

        // Check for queue pressure
        if (newSize >= _options.HighWatermark)
        {
            OnQueuePressure(new QueuePressureEventArgs
            {
                CurrentSize = (int)newSize,
                MaxSize = _options.MaxQueueSize,
                DroppedEntry = null
            });
        }

        return true;
    }

    /// <summary>
    /// Flushes all queued entries and waits for them to be written.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for flush completion.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task FlushAsync(TimeSpan timeout = default)
    {
        if (_disposed) return;

        var timeoutCts = timeout == default ?
            new CancellationTokenSource(TimeSpan.FromSeconds(30)) :
            new CancellationTokenSource(timeout);

        try
        {
            // Wait until the queue is empty or timeout occurs
            while (_queueSize > 0 && !timeoutCts.Token.IsCancellationRequested)
            {
                await Task.Delay(10, timeoutCts.Token);
            }

            // Flush the underlying provider
            await _provider.FlushAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout occurred
        }
        finally
        {
            timeoutCts.Dispose();
        }
    }

    /// <summary>
    /// Gets statistics about the async writer performance.
    /// </summary>
    /// <returns>Performance statistics.</returns>
    public AsyncLogWriterStatistics GetStatistics()
    {
        return new AsyncLogWriterStatistics
        {
            CurrentQueueSize = (int)_queueSize,
            MaxQueueSize = _options.MaxQueueSize,
            HighWatermark = _options.HighWatermark,
            IsRunning = IsRunning
        };
    }

    private async Task ProcessLogEntriesAsync()
    {
        var cancellationToken = _cancellationTokenSource.Token;
        var batch = new LogEntry[_options.BatchSize];
        var flushTimer = DateTime.UtcNow.Add(_options.FlushInterval);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Wait for entries or timeout
                await _processingSignal.WaitAsync(_options.FlushInterval, cancellationToken);

                var batchCount = 0;
                var now = DateTime.UtcNow;

                // Collect a batch of entries
                while (batchCount < _options.BatchSize && _logQueue.TryDequeue(out var entry))
                {
                    batch[batchCount++] = entry;
                    Interlocked.Decrement(ref _queueSize);
                }

                // Process the batch if we have entries or if it's time to flush
                if (batchCount > 0 || now >= flushTimer)
                {
                    await ProcessBatchAsync(batch, batchCount, cancellationToken);

                    if (now >= flushTimer)
                    {
                        await _provider.FlushAsync(cancellationToken);
                        flushTimer = now.Add(_options.FlushInterval);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Log the error but continue processing
                Console.Error.WriteLine($"Error in async log writer: {ex.Message}");
                await Task.Delay(1000, cancellationToken); // Brief pause before retrying
            }
        }

        // Process any remaining entries
        await ProcessRemainingEntriesAsync(cancellationToken);
    }

    private async Task ProcessBatchAsync(LogEntry[] batch, int count, CancellationToken cancellationToken)
    {
        for (int i = 0; i < count; i++)
        {
            try
            {
                await _provider.WriteLogAsync(batch[i], cancellationToken);
            }
            catch (Exception ex)
            {
                // Log the error but continue with other entries
                Console.Error.WriteLine($"Failed to write log entry: {ex.Message}");
            }
        }
    }

    private async Task ProcessRemainingEntriesAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Process any entries that might be in the queue
            // These entries haven't been dequeued yet, so we need to decrement as we process
            while (_logQueue.TryDequeue(out var entry))
            {
                try
                {
                    await _provider.WriteLogAsync(entry, cancellationToken);
                    Interlocked.Decrement(ref _queueSize);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to write log entry: {ex.Message}");
                    // Still decrement even on error to maintain accurate count
                    Interlocked.Decrement(ref _queueSize);
                }
            }

            await _provider.FlushAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error processing remaining log entries: {ex.Message}");
        }
    }

    private void OnQueuePressure(QueuePressureEventArgs args)
    {
        try
        {
            QueuePressure?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in queue pressure handler: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try
        {
            // Signal shutdown
            _cancellationTokenSource.Cancel();

            // Wait for processing to complete (with timeout)
            _processingTask.Wait(TimeSpan.FromSeconds(10));

            // Flush any remaining entries
            FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during async log writer disposal: {ex.Message}");
        }
        finally
        {
            _processingSignal.Dispose();
            _cancellationTokenSource.Dispose();
        }
    }
}

/// <summary>
/// Options for configuring the AsyncLogWriter.
/// </summary>
public class AsyncLogWriterOptions
{
    /// <summary>
    /// Maximum number of entries to buffer before applying overflow policy.
    /// </summary>
    public int MaxQueueSize { get; set; } = 10000;

    /// <summary>
    /// Queue size at which to raise queue pressure events.
    /// </summary>
    public int HighWatermark { get; set; } = 7500;

    /// <summary>
    /// Number of entries to process in each batch.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum time to wait before flushing buffered entries.
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Policy for handling queue overflow.
    /// </summary>
    public OverflowPolicy OverflowPolicy { get; set; } = OverflowPolicy.DropOldest;
}

/// <summary>
/// Statistics about AsyncLogWriter performance.
/// </summary>
public class AsyncLogWriterStatistics
{
    /// <summary>
    /// Current number of entries in the queue.
    /// </summary>
    public int CurrentQueueSize { get; set; }

    /// <summary>
    /// Maximum queue size.
    /// </summary>
    public int MaxQueueSize { get; set; }

    /// <summary>
    /// High watermark for queue pressure events.
    /// </summary>
    public int HighWatermark { get; set; }

    /// <summary>
    /// Whether the writer is currently running.
    /// </summary>
    public bool IsRunning { get; set; }
}

/// <summary>
/// Event arguments for queue pressure events.
/// </summary>
public class QueuePressureEventArgs : EventArgs
{
    /// <summary>
    /// Current queue size.
    /// </summary>
    public int CurrentSize { get; set; }

    /// <summary>
    /// Maximum queue size.
    /// </summary>
    public int MaxSize { get; set; }

    /// <summary>
    /// Entry that was dropped (if applicable).
    /// </summary>
    public LogEntry? DroppedEntry { get; set; }
}

/// <summary>
/// Policies for handling queue overflow.
/// </summary>
public enum OverflowPolicy
{
    /// <summary>
    /// Drop the oldest entries when queue is full.
    /// </summary>
    DropOldest,

    /// <summary>
    /// Drop the newest entries when queue is full.
    /// </summary>
    DropNewest,

    /// <summary>
    /// Block new entries when queue is full.
    /// </summary>
    Block
}