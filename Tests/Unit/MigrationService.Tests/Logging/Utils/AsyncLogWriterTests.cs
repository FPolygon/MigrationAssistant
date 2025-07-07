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
        
        // Create a new writer with a provider that blocks indefinitely
        // This ensures the processing task won't dequeue entries during our test
        var blockingProvider = new Mock<ILoggingProvider>();
        var blockingSemaphore = new SemaphoreSlim(0);
        var entriesWritten = 0;
        
        blockingProvider.Setup(x => x.IsEnabled).Returns(true);
        blockingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        blockingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                // Wait for the semaphore to be released
                await blockingSemaphore.WaitAsync(ct);
                Interlocked.Increment(ref entriesWritten);
            });
        
        // Use a custom options to ensure the processing doesn't start immediately
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 100,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromSeconds(10) // Long flush interval
        };
        
        using var writer = new AsyncLogWriter(blockingProvider.Object, options);

        // Act
        var result = writer.QueueLogEntry(entry);

        // Give the processing task a chance to start but not process
        Thread.Sleep(50);

        // Assert - the entry should be queued
        result.Should().BeTrue();
        writer.QueueSize.Should().BeGreaterThan(0);
        entriesWritten.Should().Be(0); // Verify the entry hasn't been processed yet
        
        // Clean up
        blockingSemaphore.Release();
        writer.Dispose();
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
        
        blockingProvider.Setup(x => x.IsEnabled).Returns(true);
        blockingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        blockingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                // Block to prevent dequeuing
                await blockingSemaphore.WaitAsync(ct);
            });
        
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 5,
            OverflowPolicy = OverflowPolicy.DropNewest,
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

        // Give the processing task a chance to start but ensure it's blocked
        Thread.Sleep(50);
        
        // Verify queue is full
        limitedWriter.QueueSize.Should().Be(5);

        // Act - Try to add one more
        var extraEntry = new LogEntry { Message = "Extra message" };
        var result = limitedWriter.QueueLogEntry(extraEntry);

        // Assert
        result.Should().BeFalse();
        limitedWriter.QueueSize.Should().Be(5); // Queue size should remain the same
        
        // Clean up
        blockingSemaphore.Release(5);
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
        pressureWriter.QueuePressure += (sender, args) =>
        {
            pressureEventRaised = true;
            args.CurrentSize.Should().BeGreaterOrEqualTo(8);
        };

        // Act - Fill queue to high watermark
        for (int i = 0; i < 9; i++)
        {
            var entry = new LogEntry { Message = $"Message {i}" };
            pressureWriter.QueueLogEntry(entry);
        }

        // Assert
        pressureEventRaised.Should().BeTrue();
    }

    [Fact]
    public void GetStatistics_ShouldReturnCurrentState()
    {
        // Arrange
        var entry = new LogEntry { Message = "Test message" };
        _asyncWriter.QueueLogEntry(entry);

        // Act
        var stats = _asyncWriter.GetStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.CurrentQueueSize.Should().BeGreaterThan(0);
        stats.MaxQueueSize.Should().Be(100);
        stats.HighWatermark.Should().Be(75);
        stats.IsRunning.Should().BeTrue();
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