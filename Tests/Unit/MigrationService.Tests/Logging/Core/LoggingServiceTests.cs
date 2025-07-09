using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using MigrationTool.Service.Logging.Core;
using Moq;
using Xunit;

namespace MigrationService.Tests.Logging.Core;

public class LoggingServiceTests : IDisposable
{
    private readonly LoggingService _loggingService;
    private readonly Mock<ILoggingProvider> _mockProvider;

    public LoggingServiceTests()
    {
        _loggingService = new LoggingService();
        _mockProvider = new Mock<ILoggingProvider>();
        _mockProvider.Setup(x => x.Name).Returns("TestProvider");
        _mockProvider.Setup(x => x.IsEnabled).Returns(true);
        _mockProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
    }

    [Fact]
    public void RegisterProvider_ShouldRegisterSuccessfully()
    {
        // Act
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Assert
        _loggingService.Providers.Should().ContainKey("TestProvider");
        _loggingService.Providers["TestProvider"].Should().Be(_mockProvider.Object);
    }

    [Fact]
    public void RegisterProvider_WithNullProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        var act = () => _loggingService.RegisterProvider(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void RegisterProvider_WithDuplicateName_ShouldThrowInvalidOperationException()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);

        var duplicateProvider = new Mock<ILoggingProvider>();
        duplicateProvider.Setup(x => x.Name).Returns("TestProvider");

        // Act & Assert
        var act = () => _loggingService.RegisterProvider(duplicateProvider.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("A provider with name 'TestProvider' is already registered.");
    }

    [Fact]
    public void UnregisterProvider_ShouldRemoveProviderAndReturnTrue()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        var result = _loggingService.UnregisterProvider("TestProvider");

        // Assert
        result.Should().BeTrue();
        _loggingService.Providers.Should().NotContainKey("TestProvider");
        _mockProvider.Verify(x => x.Dispose(), Times.Once);
    }

    [Fact]
    public void UnregisterProvider_WithNonExistentProvider_ShouldReturnFalse()
    {
        // Act
        var result = _loggingService.UnregisterProvider("NonExistent");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfigureAsync_ShouldConfigureAllProviders()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);
        var settings = new LoggingSettings { MinimumLevel = LogLevel.Debug };

        // Act
        await _loggingService.ConfigureAsync(settings);

        // Assert
        _loggingService.Settings.Should().Be(settings);
        _mockProvider.Verify(x => x.Configure(settings), Times.Once);
    }

    [Fact]
    public async Task LogAsync_ShouldWriteToEnabledProviders()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        await _loggingService.LogAsync(LogLevel.Information, "TestCategory", "Test message");

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(
            It.Is<LogEntry>(e =>
                e.Level == LogLevel.Information &&
                e.Category == "TestCategory" &&
                e.Message == "Test message"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithProperties_ShouldIncludeProperties()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);
        var properties = new Dictionary<string, object?> { ["Key1"] = "Value1", ["Key2"] = 42 };

        // Act
        await _loggingService.LogAsync(LogLevel.Warning, "TestCategory", "Test message", properties);

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(
            It.Is<LogEntry>(e =>
                e.Properties.ContainsKey("Key1") &&
                e.Properties["Key1"]!.Equals("Value1") &&
                e.Properties.ContainsKey("Key2") &&
                e.Properties["Key2"]!.Equals(42)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogPerformanceAsync_ShouldCreatePerformanceEntry()
    {
        // Arrange
        _loggingService.RegisterProvider(_mockProvider.Object);
        var customMetrics = new Dictionary<string, double> { ["Throughput"] = 100.5 };

        // Act
        await _loggingService.LogPerformanceAsync("TestCategory", "TestOperation", 250.5, customMetrics);

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(
            It.Is<LogEntry>(e =>
                e.Level == LogLevel.Information &&
                e.Category == "TestCategory" &&
                e.Message == "Performance: TestOperation" &&
                e.Performance != null &&
                e.Performance.DurationMs == 250.5 &&
                e.Performance.CustomMetrics.ContainsKey("Throughput")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void CreateLogger_ShouldReturnLoggerWithCorrectCategory()
    {
        // Act
        var logger = _loggingService.CreateLogger("TestCategory");

        // Assert
        logger.Should().NotBeNull();
        logger.Category.Should().Be("TestCategory");
    }

    [Fact]
    public void CreateLogger_Generic_ShouldReturnLoggerWithTypeFullName()
    {
        // Act
        var logger = _loggingService.CreateLogger<LoggingServiceTests>();

        // Assert
        logger.Should().NotBeNull();
        logger.Category.Should().Be(typeof(LoggingServiceTests).FullName);
    }

    [Fact]
    public async Task FlushAsync_ShouldFlushAllEnabledProviders()
    {
        // Arrange
        var provider1 = new Mock<ILoggingProvider>();
        provider1.Setup(x => x.Name).Returns("Provider1");
        provider1.Setup(x => x.IsEnabled).Returns(true);

        var provider2 = new Mock<ILoggingProvider>();
        provider2.Setup(x => x.Name).Returns("Provider2");
        provider2.Setup(x => x.IsEnabled).Returns(false);

        _loggingService.RegisterProvider(provider1.Object);
        _loggingService.RegisterProvider(provider2.Object);

        // Act
        await _loggingService.FlushAsync();

        // Assert
        provider1.Verify(x => x.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
        provider2.Verify(x => x.FlushAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogAsync_WithDisabledProvider_ShouldNotWriteToProvider()
    {
        // Arrange
        _mockProvider.Setup(x => x.IsEnabled).Returns(false);
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        await _loggingService.LogAsync(LogLevel.Information, "TestCategory", "Test message");

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogAsync_WithLevelBelowMinimum_ShouldNotWriteToProvider()
    {
        // Arrange
        var settings = new LoggingSettings { MinimumLevel = LogLevel.Warning };
        await _loggingService.ConfigureAsync(settings);
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        await _loggingService.LogAsync(LogLevel.Information, "TestCategory", "Test message");

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LogAsync_WithCategoryOverride_ShouldRespectCategoryLevel()
    {
        // Arrange
        var settings = new LoggingSettings
        {
            MinimumLevel = LogLevel.Warning,
            CategoryOverrides = new Dictionary<string, LogLevel> { ["TestCategory"] = LogLevel.Debug }
        };
        await _loggingService.ConfigureAsync(settings);
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        await _loggingService.LogAsync(LogLevel.Information, "TestCategory", "Test message");

        // Assert
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_WithProviderException_ShouldContinueToOtherProviders()
    {
        // Arrange
        var failingProvider = new Mock<ILoggingProvider>();
        failingProvider.Setup(x => x.Name).Returns("FailingProvider");
        failingProvider.Setup(x => x.IsEnabled).Returns(true);
        failingProvider.Setup(x => x.IsLevelEnabled(It.IsAny<LogLevel>())).Returns(true);
        failingProvider.Setup(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test exception"));

        _loggingService.RegisterProvider(failingProvider.Object);
        _loggingService.RegisterProvider(_mockProvider.Object);

        // Act
        await _loggingService.LogAsync(LogLevel.Information, "TestCategory", "Test message");

        // Assert
        failingProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockProvider.Verify(x => x.WriteLogAsync(It.IsAny<LogEntry>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    public void Dispose()
    {
        _loggingService.Dispose();
    }
}
