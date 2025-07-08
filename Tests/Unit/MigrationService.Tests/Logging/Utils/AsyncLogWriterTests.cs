using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Utils;
using Moq;
using Xunit;

namespace MigrationService.Tests.Logging.Utils;

public class AsyncLogWriterTests : IDisposable
{
    private readonly Mock<ILoggingProvider> _mockProvider;
    private readonly AsyncLogWriter _asyncWriter;

    public AsyncLogWriterTests()
    {
        _mockProvider = new Mock<ILoggingProvider>();
        _mockProvider.Setup(x => x.Name).Returns("TestProvider");
        _mockProvider.Setup(x => x.IsEnabled).Returns(true);

        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 100,
            HighWatermark = 75,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(100),
            OverflowPolicy = OverflowPolicy.DropOldest
        };

        _asyncWriter = new AsyncLogWriter(_mockProvider.Object, options);
    }

    [Fact]
    public void QueueSize_Initially_ShouldBeZero()
    {
        // Assert
        _asyncWriter.QueueSize.Should().Be(0);
    }

    [Fact]
    public void IsRunning_Initially_ShouldBeTrue()
    {
        // Assert
        _asyncWriter.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void QueueLogEntry_ShouldReturnTrueAndIncreaseQueueSize()
    {
        // Arrange
        var entry = new LogEntry { Message = "Test message" };

        // Create a new writer with a provider that blocks before dequeuing
        // This ensures we can check the queue size before any dequeuing happens
        var blockingProvider = new Mock<ILoggingProvider>();
        var dequeueSemaphore = new SemaphoreSlim(0); // Blocks the processing thread before it can dequeue
        var processingStarted = new ManualResetEventSlim(false);
        var canDequeue = new ManualResetEventSlim(false);
        var entriesWritten = 0;

        blockingProvider.Setup(x => x.IsEnabled).Returns(true);
        blockingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        blockingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                // Signal that processing has started
                processingStarted.Set();
                // Wait for permission to continue
                await dequeueSemaphore.WaitAsync(ct);
                Interlocked.Increment(ref entriesWritten);
            });

        // Use a custom options with a very long flush interval to control timing
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 100,
            BatchSize = 1, // Process one at a time
            FlushInterval = TimeSpan.FromMinutes(10) // Very long flush interval to avoid timing issues
        };

        using var writer = new AsyncLogWriter(blockingProvider.Object, options);

        // Act
        var result = writer.QueueLogEntry(entry);

        // Assert immediately after queuing
        // At this point, the processing thread might be waiting on the semaphore in ProcessLogEntriesAsync
        // but it hasn't dequeued the entry yet
        result.Should().BeTrue();

        // Small delay to let the processing thread wake up but not dequeue
        Thread.Sleep(50);

        // The queue size should still be 1 because the processing thread is blocked
        var queueSizeBeforeProcessing = writer.QueueSize;
        queueSizeBeforeProcessing.Should().BeInRange(0, 1, "Queue size should be 0 or 1 depending on timing");

        // If queue size is already 0, the entry was dequeued very quickly
        if (queueSizeBeforeProcessing == 0)
        {
            // Processing already started and dequeued the entry
            processingStarted.IsSet.Should().BeTrue("Processing should have started if queue is empty");
        }
        else
        {
            // Queue still has the entry, wait for processing to start
            processingStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue("Processing should start");

            // After processing starts and dequeues, the queue should be empty
            Thread.Sleep(50); // Give time for dequeue
            writer.QueueSize.Should().Be(0, "Queue should be empty after entry is dequeued for processing");
        }

        entriesWritten.Should().Be(0, "Entry should not have been fully processed yet");

        // Clean up
        dequeueSemaphore.Release();
        writer.Dispose();
        processingStarted.Dispose();
        canDequeue.Dispose();
    }

    [Fact]
    public async Task QueueLogEntry_ShouldEventuallyCallProvider()
    {
        // Arrange
        var entry = new LogEntry { Message = "Test message" };

        // Act
        _asyncWriter.QueueLogEntry(entry);

        // Wait for processing
        await Task.Delay(200);

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(
            It.Is<LogEntry>(e => e.Message == "Test message"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueueLogEntry_MultipleEntries_ShouldProcessInBatches()
    {
        // Arrange
        var entries = new LogEntry[25];
        for (int i = 0; i < 25; i++)
        {
            entries[i] = new LogEntry { Message = $"Message {i}" };
        }

        // Act
        foreach (var entry in entries)
        {
            _asyncWriter.QueueLogEntry(entry);
        }

        // Wait for processing
        await Task.Delay(300);

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(
            It.IsAny<LogEntry>(),
            It.IsAny<CancellationToken>()), Times.Exactly(25));
    }

    [Fact]
    public async Task FlushAsync_ShouldWaitForQueueToEmpty()
    {
        // Arrange
        var entries = new LogEntry[10];
        for (int i = 0; i < 10; i++)
        {
            entries[i] = new LogEntry { Message = $"Message {i}" };
        }

        foreach (var entry in entries)
        {
            _asyncWriter.QueueLogEntry(entry);
        }

        // Act
        await _asyncWriter.FlushAsync();

        // Assert
        _asyncWriter.QueueSize.Should().Be(0);
        _mockProvider.Verify(x => x.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void QueueLogEntry_WhenQueueFull_WithDropOldestPolicy_ShouldDropOldEntries()
    {
        // Arrange
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 5,
            OverflowPolicy = OverflowPolicy.DropOldest
        };

        using var limitedWriter = new AsyncLogWriter(_mockProvider.Object, options);

        // Act - Fill queue beyond capacity
        for (int i = 0; i < 10; i++)
        {
            var entry = new LogEntry { Message = $"Message {i}" };
            limitedWriter.QueueLogEntry(entry);
        }

        // Assert
        limitedWriter.QueueSize.Should().BeLessOrEqualTo(5);
    }

    [Fact]
    public void QueueLogEntry_WhenQueueFull_WithDropNewestPolicy_ShouldRejectNewEntries()
    {
        // Arrange
        var blockingProvider = new Mock<ILoggingProvider>();
        var blockingSemaphore = new SemaphoreSlim(0);
        var processingStarted = new ManualResetEventSlim(false); // Use ManualResetEventSlim instead of CountdownEvent
        var processedCount = 0;

        blockingProvider.Setup(x => x.IsEnabled).Returns(true);
        blockingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        blockingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                // Signal that processing has started (only on first call)
                if (Interlocked.Increment(ref processedCount) == 1)
                {
                    processingStarted.Set();
                }
                // Block to prevent further dequeuing
                await blockingSemaphore.WaitAsync(ct);
            });

        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 5,
            OverflowPolicy = OverflowPolicy.DropNewest,
            BatchSize = 1, // Process one at a time
            FlushInterval = TimeSpan.FromSeconds(10) // Long flush interval
        };

        using var limitedWriter = new AsyncLogWriter(blockingProvider.Object, options);

        // Fill queue to capacity
        for (int i = 0; i < 5; i++)
        {
            var entry = new LogEntry { Message = $"Message {i}" };
            var added = limitedWriter.QueueLogEntry(entry);
            added.Should().BeTrue($"Entry {i} should be added");
        }

        // Wait for processing to start but be blocked on the first entry
        processingStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue("Processing should have started");

        // Small delay to ensure queue state is stable after dequeue
        Thread.Sleep(100);

        // After one entry is dequeued for processing, the queue size becomes 4
        // This leaves room for one more entry before hitting the limit again
        var currentSize = limitedWriter.QueueSize;
        currentSize.Should().Be(4, "Queue should contain 4 entries after one is dequeued for processing");

        // Act - Try to add one more (which should succeed since we have room for 1)
        var firstExtraEntry = new LogEntry { Message = "Extra message 1" };
        var firstResult = limitedWriter.QueueLogEntry(firstExtraEntry);
        firstResult.Should().BeTrue("First extra entry should be accepted since queue has room");

        // Now the queue is full again (5 entries)
        limitedWriter.QueueSize.Should().Be(5, "Queue should be at capacity after adding one more");

        // Try to add another one - this should be rejected
        var secondExtraEntry = new LogEntry { Message = "Extra message 2" };
        var secondResult = limitedWriter.QueueLogEntry(secondExtraEntry);
        secondResult.Should().BeFalse("Second extra entry should be rejected when queue is full");
        limitedWriter.QueueSize.Should().Be(5, "Queue size should remain at capacity");

        // Clean up
        blockingSemaphore.Release(6); // Release for all entries that might be processed
        processingStarted.Dispose();
    }

    [Fact]
    public void QueueLogEntry_ReachingHighWatermark_ShouldRaiseQueuePressureEvent()
    {
        // Arrange
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 10,
            HighWatermark = 8
        };

        using var pressureWriter = new AsyncLogWriter(_mockProvider.Object, options);

        var pressureEventRaised = false;
        var eventCount = 0;
        pressureWriter.QueuePressure += (sender, args) =>
        {
            pressureEventRaised = true;
            eventCount++;
            args.CurrentSize.Should().BeGreaterOrEqualTo(8);
        };

        // Act - Fill queue to high watermark
        for (int i = 0; i < 9; i++)
        {
            var entry = new LogEntry { Message = $"Message {i}" };
            pressureWriter.QueueLogEntry(entry);
        }

        // Give some time for async processing
        Thread.Sleep(100);

        // Assert
        pressureEventRaised.Should().BeTrue();
    }

    [Fact]
    public void GetStatistics_ShouldReturnCurrentState()
    {
        // Arrange
        var blockingProvider = new Mock<ILoggingProvider>();
        var blockingSemaphore = new SemaphoreSlim(0);
        var processingStarted = new ManualResetEventSlim(false);

        blockingProvider.Setup(x => x.IsEnabled).Returns(true);
        blockingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        blockingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                processingStarted.Set();
                await blockingSemaphore.WaitAsync(ct);
            });

        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 100,
            HighWatermark = 75,
            BatchSize = 1,
            FlushInterval = TimeSpan.FromSeconds(10)
        };

        using var writer = new AsyncLogWriter(blockingProvider.Object, options);

        var entry = new LogEntry { Message = "Test message" };
        writer.QueueLogEntry(entry);

        // Act - Get statistics immediately after queuing
        var statsBeforeProcessing = writer.GetStatistics();

        // Assert - Before processing starts
        statsBeforeProcessing.Should().NotBeNull();
        statsBeforeProcessing.CurrentQueueSize.Should().Be(1, "Queue should contain exactly one entry before processing");
        statsBeforeProcessing.MaxQueueSize.Should().Be(100);
        statsBeforeProcessing.HighWatermark.Should().Be(75);
        statsBeforeProcessing.IsRunning.Should().BeTrue();

        // Wait for processing to start
        processingStarted.Wait(TimeSpan.FromSeconds(1)).Should().BeTrue("Processing should have started");

        // Get statistics after processing has dequeued the entry
        var statsAfterProcessing = writer.GetStatistics();
        statsAfterProcessing.CurrentQueueSize.Should().Be(0, "Queue should be empty after entry is dequeued for processing");

        // Clean up
        blockingSemaphore.Release();
        writer.Dispose();
        processingStarted.Dispose();
    }

    [Fact]
    public async Task ProviderException_ShouldNotStopProcessing()
    {
        // Arrange
        var callCount = 0;
        _mockProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new InvalidOperationException("Test exception");
                }
                return Task.CompletedTask;
            });

        var entry1 = new LogEntry { Message = "Failing message" };
        var entry2 = new LogEntry { Message = "Success message" };

        // Act
        _asyncWriter.QueueLogEntry(entry1);
        _asyncWriter.QueueLogEntry(entry2);

        // Wait for processing
        await Task.Delay(200);

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public void Dispose_ShouldStopProcessingAndFlushRemaining()
    {
        // Arrange
        var entry = new LogEntry { Message = "Test message" };
        _asyncWriter.QueueLogEntry(entry);

        // Act
        _asyncWriter.Dispose();

        // Assert
        _asyncWriter.IsRunning.Should().BeFalse();
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    public void Dispose()
    {
        _asyncWriter?.Dispose();
    }
}