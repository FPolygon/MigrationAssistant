using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using Moq;
using Xunit;

namespace MigrationService.Tests;

public class ServiceManagerTests
{
    private readonly Mock<ILogger<ServiceManager>> _loggerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IOptions<ServiceConfiguration>> _configMock;
    private readonly ServiceManager _serviceManager;
    private readonly ServiceConfiguration _configuration;

    public ServiceManagerTests()
    {
        _loggerMock = new Mock<ILogger<ServiceManager>>();
        _stateManagerMock = new Mock<IStateManager>();
        _configMock = new Mock<IOptions<ServiceConfiguration>>();
        
        _configuration = new ServiceConfiguration
        {
            DataPath = @"C:\Test\Data",
            LogPath = @"C:\Test\Logs",
            StateCheckIntervalSeconds = 60
        };
        
        _configMock.Setup(x => x.Value).Returns(_configuration);
        
        _serviceManager = new ServiceManager(
            _loggerMock.Object,
            _stateManagerMock.Object,
            _configMock.Object);
    }

    [Fact]
    public async Task InitializeAsync_ShouldInitializeSuccessfully()
    {
        // Act
        await _serviceManager.InitializeAsync(CancellationToken.None);
        
        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("initialized successfully")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyInitialized_ShouldLogWarning()
    {
        // Arrange
        await _serviceManager.InitializeAsync(CancellationToken.None);
        
        // Act
        await _serviceManager.InitializeAsync(CancellationToken.None);
        
        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("already initialized")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task PerformHealthCheckAsync_ShouldCheckDatabaseHealth()
    {
        // Arrange
        _stateManagerMock.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        // Act
        await _serviceManager.PerformHealthCheckAsync(CancellationToken.None);
        
        // Assert
        _stateManagerMock.Verify(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PerformHealthCheckAsync_WhenDatabaseUnhealthy_ShouldLogWarning()
    {
        // Arrange
        _stateManagerMock.Setup(x => x.CheckHealthAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        
        // Act
        await _serviceManager.PerformHealthCheckAsync(CancellationToken.None);
        
        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Database health check failed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckMigrationStatusAsync_ShouldProcessActiveMigrations()
    {
        // Arrange
        var migrations = new List<MigrationState>
        {
            new MigrationState { UserId = "user1", AttentionReason = "" },
            new MigrationState { UserId = "user2", AttentionReason = "Quota exceeded" }
        };
        
        _stateManagerMock.Setup(x => x.GetActiveMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(migrations);
        
        // Act
        await _serviceManager.CheckMigrationStatusAsync(CancellationToken.None);
        
        // Assert
        _stateManagerMock.Verify(x => x.GetActiveMigrationsAsync(It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("requires attention")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckMigrationStatusAsync_ShouldCheckResetReadiness()
    {
        // Arrange
        _stateManagerMock.Setup(x => x.GetActiveMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<MigrationState>());
        
        _stateManagerMock.Setup(x => x.AreAllUsersReadyForResetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        
        // Act
        await _serviceManager.CheckMigrationStatusAsync(CancellationToken.None);
        
        // Assert
        _stateManagerMock.Verify(x => x.AreAllUsersReadyForResetAsync(It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ready for system reset")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupAsync_ShouldFlushStateAndLogCompletion()
    {
        // Act
        await _serviceManager.CleanupAsync(CancellationToken.None);
        
        // Assert
        _stateManagerMock.Verify(x => x.FlushAsync(It.IsAny<CancellationToken>()), Times.Once);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("cleanup completed")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CleanupAsync_WhenFlushFails_ShouldStillComplete()
    {
        // Arrange
        _stateManagerMock.Setup(x => x.FlushAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Flush failed"));
        
        // Act
        await _serviceManager.CleanupAsync(CancellationToken.None);
        
        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Error during service cleanup")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}