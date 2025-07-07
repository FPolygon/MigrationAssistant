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
        var processingBlocker = new SemaphoreSlim(0, 1);
        
        // Block the provider from processing entries immediately
        _mockProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .Returns(async (LogEntry e, CancellationToken ct) =>
            {
                await processingBlocker.WaitAsync(ct);
            });

        // Act
        var result = _asyncWriter.QueueLogEntry(entry);

        // Assert
        result.Should().BeTrue();
        _asyncWriter.QueueSize.Should().Be(1);
        
        // Clean up
        processingBlocker.Release();
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
        var options = new AsyncLogWriterOptions
        {
            MaxQueueSize = 5,
            OverflowPolicy = OverflowPolicy.DropNewest
        };

        using var limitedWriter = new AsyncLogWriter(_mockProvider.Object, options);

        // Fill queue to capacity
        for (int i = 0; i < 5; i++)
        {
            var entry = new LogEntry { Message = $"Message {i}" };
            limitedWriter.QueueLogEntry(entry);
        }

        // Act - Try to add one more
        var extraEntry = new LogEntry { Message = "Extra message" };
        var result = limitedWriter.QueueLogEntry(extraEntry);

        // Assert
        result.Should().BeFalse();
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