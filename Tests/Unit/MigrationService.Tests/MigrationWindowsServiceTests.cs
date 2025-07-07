using FluentAssertions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service;
using MigrationTool.Service.Core;
using Moq;
using Xunit;

namespace MigrationService.Tests;

public class MigrationWindowsServiceTests : IDisposable
{
    private readonly Mock<ILogger<MigrationWindowsService>> _loggerMock;
    private readonly Mock<IServiceManager> _serviceManagerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IIpcServer> _ipcServerMock;
    private readonly Mock<IOptions<ServiceConfiguration>> _configMock;
    private readonly Mock<IHostApplicationLifetime> _lifetimeMock;
    private readonly MigrationWindowsService _service;
    private readonly ServiceConfiguration _configuration;
    private readonly string _testDataPath;

    public MigrationWindowsServiceTests()
    {
        _loggerMock = new Mock<ILogger<MigrationWindowsService>>();
        _stateManagerMock = new Mock<IStateManager>();
        _ipcServerMock = new Mock<IIpcServer>();
        _configMock = new Mock<IOptions<ServiceConfiguration>>();
        _lifetimeMock = new Mock<IHostApplicationLifetime>();

        // Create a test directory
        _testDataPath = Path.Combine(Path.GetTempPath(), $"MigrationServiceTest_{Guid.NewGuid()}");

        _configuration = new ServiceConfiguration
        {
            DataPath = Path.Combine(_testDataPath, "Data"),
            LogPath = Path.Combine(_testDataPath, "Logs"),
            StateCheckIntervalSeconds = 1 // Short interval for testing
        };

        _configMock.Setup(x => x.Value).Returns(_configuration);

        // Create a mock for IServiceManager
        _serviceManagerMock = new Mock<IServiceManager>();

        _serviceManagerMock.Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _serviceManagerMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _serviceManagerMock.Setup(x => x.CheckMigrationStatusAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _serviceManagerMock.Setup(x => x.CleanupAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _service = new MigrationWindowsService(
            _loggerMock.Object,
            _serviceManagerMock.Object,
            _stateManagerMock.Object,
            _ipcServerMock.Object,
            _configMock.Object,
            _lifetimeMock.Object);
    }

    public void Dispose()
    {
        // Cleanup test directory
        if (Directory.Exists(_testDataPath))
        {
            Directory.Delete(_testDataPath, true);
        }
    }

    [Fact]
    public async Task StartAsync_ShouldCreateDirectories()
    {
        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        Directory.Exists(_configuration.DataPath).Should().BeTrue();
        Directory.Exists(_configuration.LogPath).Should().BeTrue();
        Directory.Exists(Path.Combine(_configuration.DataPath, "Backups")).Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_ShouldLogStartMessage()
    {
        // Act
        await _service.StartAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("OnStart called")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldStopIpcServer()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Act
        await _service.StopAsync(cts.Token);

        // Assert
        _ipcServerMock.Verify(x => x.StopAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldCallCleanup()
    {
        // Act
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _serviceManagerMock.Verify(x => x.CleanupAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopAsync_ShouldLogStopMessage()
    {
        // Act
        await _service.StopAsync(CancellationToken.None);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("stopped")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldInitializeComponents()
    {
        // Arrange
        var cts = new CancellationTokenSource();

        // Start the service and let it run briefly
        var executeTask = _service.StartAsync(cts.Token);
        await Task.Delay(100);

        // Act
        cts.Cancel();
        await executeTask;

        // Assert
        _stateManagerMock.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        _serviceManagerMock.Verify(x => x.InitializeAsync(It.IsAny<CancellationToken>()), Times.Once);
        _ipcServerMock.Verify(x => x.StartAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInitializationFails_ShouldStopApplication()
    {
        // Arrange
        _stateManagerMock.Setup(x => x.InitializeAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Initialization failed"));

        var cts = new CancellationTokenSource();

        // Act
        var executeTask = _service.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        try
        {
            await executeTask;
        }
        catch
        {
            // Expected
        }

        // Assert
        _lifetimeMock.Verify(x => x.StopApplication(), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPerformPeriodicHealthChecks()
    {
        // Arrange
        var healthCheckCount = 0;
        var completionSource = new TaskCompletionSource<bool>();

        _serviceManagerMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                healthCheckCount++;
                if (healthCheckCount >= 2)
                {
                    completionSource.TrySetResult(true);
                }
                return Task.CompletedTask;
            });

        var cts = new CancellationTokenSource();

        // Act
        await _service.StartAsync(cts.Token);

        // Wait for at least 2 health checks or timeout
        var timeoutTask = Task.Delay(5000);
        var completedTask = await Task.WhenAny(completionSource.Task, timeoutTask);

        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        completedTask.Should().Be(completionSource.Task, "Health check should have been performed at least twice");
        _serviceManagerMock.Verify(
            x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenHealthCheckFails_ShouldContinueRunning()
    {
        // Arrange
        var callCount = 0;
        var healthCheckCompletionSource = new TaskCompletionSource<bool>();

        _serviceManagerMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new Exception("Health check failed");
                }
                if (callCount >= 2)
                {
                    healthCheckCompletionSource.TrySetResult(true);
                }
                return Task.CompletedTask;
            });

        var cts = new CancellationTokenSource();

        // Act
        await _service.StartAsync(cts.Token);

        // Give the service a moment to start its main loop
        await Task.Delay(100);

        // Wait for at least 2 health checks or timeout
        var timeoutTask = Task.Delay(10000); // Increased timeout to 10 seconds
        var completedTask = await Task.WhenAny(healthCheckCompletionSource.Task, timeoutTask);

        cts.Cancel();
        await _service.StopAsync(CancellationToken.None);

        // Assert
        completedTask.Should().Be(healthCheckCompletionSource.Task, "Health check should have been called multiple times");
        callCount.Should().BeGreaterThan(1);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error in service main loop")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }
}