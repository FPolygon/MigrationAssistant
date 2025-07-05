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
    private readonly Mock<ServiceManager> _serviceManagerMock;
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
        
        // Create a partial mock for ServiceManager to allow testing
        var serviceManagerLogger = new Mock<ILogger<ServiceManager>>();
        _serviceManagerMock = new Mock<ServiceManager>(
            serviceManagerLogger.Object,
            _stateManagerMock.Object,
            _configMock.Object);
        
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
        var cts = new CancellationTokenSource();
        
        // Start the service and let it run for multiple check intervals
        var executeTask = _service.StartAsync(cts.Token);
        await Task.Delay(2500); // Run for 2.5 seconds with 1-second interval
        
        // Act
        cts.Cancel();
        await executeTask;
        
        // Assert
        _serviceManagerMock.Verify(
            x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()), 
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WhenHealthCheckFails_ShouldContinueRunning()
    {
        // Arrange
        var callCount = 0;
        _serviceManagerMock.Setup(x => x.PerformHealthCheckAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new Exception("Health check failed");
                }
                return Task.CompletedTask;
            });
        
        var cts = new CancellationTokenSource();
        
        // Act
        var executeTask = _service.StartAsync(cts.Token);
        await Task.Delay(2500); // Let it run for multiple intervals
        cts.Cancel();
        await executeTask;
        
        // Assert
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