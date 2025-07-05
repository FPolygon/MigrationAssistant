using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using Moq;
using Xunit;

namespace MigrationService.Tests;

public class MigrationStateOrchestratorTests
{
    private readonly Mock<ILogger<MigrationStateOrchestrator>> _loggerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly MigrationStateOrchestrator _orchestrator;

    public MigrationStateOrchestratorTests()
    {
        _loggerMock = new Mock<ILogger<MigrationStateOrchestrator>>();
        _stateManagerMock = new Mock<IStateManager>();
        _orchestrator = new MigrationStateOrchestrator(_loggerMock.Object, _stateManagerMock.Object);
    }

    [Fact]
    public async Task ProcessAutomaticTransitionsAsync_ShouldProcessAllActiveMigrations()
    {
        // Arrange
        var migrations = new List<MigrationState>
        {
            new MigrationState { UserId = "user1", State = MigrationStateType.Initializing },
            new MigrationState { UserId = "user2", State = MigrationStateType.BackupCompleted }
        };

        _stateManagerMock.Setup(x => x.GetActiveMigrationsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(migrations);

        // Act
        await _orchestrator.ProcessAutomaticTransitionsAsync(CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.GetActiveMigrationsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_WithTimeout_ShouldHandleTimeout()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.BackupInProgress,
            LastUpdated = DateTime.UtcNow.AddDays(-2) // 2 days old, should timeout
        };

        _stateManagerMock.Setup(x => x.TransitionStateAsync(
            It.IsAny<string>(), MigrationStateType.Failed, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.TransitionStateAsync(
            "user1", MigrationStateType.Failed, It.Is<string>(s => s.Contains("timed out")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_WithAutomaticTransition_ShouldTransition()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.Initializing,
            LastUpdated = DateTime.UtcNow
        };

        _stateManagerMock.Setup(x => x.TransitionStateAsync(
            It.IsAny<string>(), MigrationStateType.WaitingForUser, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.TransitionStateAsync(
            "user1", MigrationStateType.WaitingForUser, 
            It.Is<string>(s => s.Contains("Automatic transition")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_BackupInProgress_WithCompletedOps_ShouldTransition()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.BackupInProgress,
            LastUpdated = DateTime.UtcNow
        };

        var backupOps = new List<BackupOperation>
        {
            new BackupOperation
            {
                Status = BackupStatus.Completed,
                StartedAt = DateTime.UtcNow
            }
        };

        _stateManagerMock.Setup(x => x.GetUserBackupOperationsAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupOps);

        _stateManagerMock.Setup(x => x.TransitionStateAsync(
            It.IsAny<string>(), MigrationStateType.BackupCompleted, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.TransitionStateAsync(
            "user1", MigrationStateType.BackupCompleted, 
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_ShouldCheckForEscalation()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.Failed,
            LastUpdated = DateTime.UtcNow
        };

        // Mock multiple failed operations
        var failedOps = Enumerable.Range(0, 3).Select(i => new BackupOperation
        {
            Status = BackupStatus.Failed,
            StartedAt = DateTime.UtcNow.AddHours(-i)
        }).ToList();

        _stateManagerMock.Setup(x => x.GetUserBackupOperationsAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOps);

        _stateManagerMock.Setup(x => x.GetUserEscalationsAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ITEscalation>());

        _stateManagerMock.Setup(x => x.CreateEscalationAsync(It.IsAny<ITEscalation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.CreateEscalationAsync(
            It.Is<ITEscalation>(e => e.AutoTriggered == true), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleBackupCompletedAsync_WithAllCategoriesComplete_ShouldTransitionToCompleted()
    {
        // Arrange
        var userId = "user1";
        var migration = new MigrationState
        {
            UserId = userId,
            State = MigrationStateType.BackupInProgress
        };

        _stateManagerMock.Setup(x => x.GetMigrationStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);

        var completedOps = new[]
        {
            new BackupOperation { Category = "files", Status = BackupStatus.Completed },
            new BackupOperation { Category = "browsers", Status = BackupStatus.Completed },
            new BackupOperation { Category = "email", Status = BackupStatus.Completed },
            new BackupOperation { Category = "system", Status = BackupStatus.Completed }
        };

        _stateManagerMock.Setup(x => x.GetUserBackupOperationsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(completedOps);

        _stateManagerMock.Setup(x => x.TransitionStateAsync(
            It.IsAny<string>(), MigrationStateType.BackupCompleted, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.HandleBackupCompletedAsync(userId, "op123", true, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.TransitionStateAsync(
            userId, MigrationStateType.BackupCompleted,
            It.Is<string>(s => s.Contains("All backup categories completed")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleBackupCompletedAsync_WithPartialCompletion_ShouldUpdateProgress()
    {
        // Arrange
        var userId = "user1";
        var migration = new MigrationState
        {
            UserId = userId,
            State = MigrationStateType.BackupInProgress,
            Progress = 25
        };

        _stateManagerMock.Setup(x => x.GetMigrationStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);

        var partialOps = new[]
        {
            new BackupOperation { Category = "files", Status = BackupStatus.Completed },
            new BackupOperation { Category = "browsers", Status = BackupStatus.Completed }
        };

        _stateManagerMock.Setup(x => x.GetUserBackupOperationsAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(partialOps);

        // Act
        await _orchestrator.HandleBackupCompletedAsync(userId, "op123", true, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.UpdateMigrationStateAsync(
            It.Is<MigrationState>(m => m.Progress == 50), // 2 out of 4 categories
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleBackupCompletedAsync_WithFailureAfterRetries_ShouldFailMigration()
    {
        // Arrange
        var userId = "user1";
        var operationId = "op123";
        
        var migration = new MigrationState
        {
            UserId = userId,
            State = MigrationStateType.BackupInProgress
        };

        _stateManagerMock.Setup(x => x.GetMigrationStateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(migration);

        var failedOp = new BackupOperation
        {
            OperationId = operationId,
            Status = BackupStatus.Failed,
            RetryCount = 3
        };

        _stateManagerMock.Setup(x => x.GetBackupOperationAsync(operationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedOp);

        _stateManagerMock.Setup(x => x.TransitionStateAsync(
            It.IsAny<string>(), MigrationStateType.Failed, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _orchestrator.HandleBackupCompletedAsync(userId, operationId, false, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.TransitionStateAsync(
            userId, MigrationStateType.Failed,
            It.Is<string>(s => s.Contains("failed after 3 retries")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldCalculateCorrectStatistics()
    {
        // Arrange
        var summaries = new List<UserMigrationSummary>
        {
            new UserMigrationSummary { State = MigrationStateType.NotStarted, TotalBackupSizeMB = 100 },
            new UserMigrationSummary { State = MigrationStateType.BackupInProgress, TotalBackupSizeMB = 200 },
            new UserMigrationSummary { State = MigrationStateType.ReadyForReset, TotalBackupSizeMB = 300 },
            new UserMigrationSummary { State = MigrationStateType.Failed, TotalBackupSizeMB = 0 },
            new UserMigrationSummary { State = MigrationStateType.Escalated, TotalBackupSizeMB = 50 }
        };

        _stateManagerMock.Setup(x => x.GetMigrationSummariesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(summaries);

        // Act
        var stats = await _orchestrator.GetStatisticsAsync(CancellationToken.None);

        // Assert
        stats.TotalUsers.Should().Be(5);
        stats.NotStarted.Should().Be(1);
        stats.InProgress.Should().Be(1);
        stats.Completed.Should().Be(1);
        stats.Failed.Should().Be(1);
        stats.Escalated.Should().Be(1);
        stats.TotalDataSizeMB.Should().Be(650);
        stats.CompletionPercentage.Should().Be(20); // 1 completed out of 5 total
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_WithQuotaIssue_ShouldEscalate()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.SyncInProgress,
            LastUpdated = DateTime.UtcNow
        };

        var syncStatus = new OneDriveSyncStatus
        {
            QuotaAvailableMB = 500, // Less than 1000MB threshold
            ErrorCount = 0
        };

        _stateManagerMock.Setup(x => x.GetOneDriveSyncStatusAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncStatus);

        _stateManagerMock.Setup(x => x.CreateEscalationAsync(
            It.Is<ITEscalation>(e => e.TriggerType == EscalationTriggerType.QuotaExceeded), 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.CreateEscalationAsync(
            It.Is<ITEscalation>(e => e.TriggerType == EscalationTriggerType.QuotaExceeded),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserTransitionAsync_WithPersistentSyncErrors_ShouldEscalate()
    {
        // Arrange
        var migration = new MigrationState
        {
            UserId = "user1",
            State = MigrationStateType.SyncInProgress,
            LastUpdated = DateTime.UtcNow
        };

        var syncStatus = new OneDriveSyncStatus
        {
            ErrorCount = 10, // More than 5 errors threshold
            QuotaAvailableMB = 5000
        };

        _stateManagerMock.Setup(x => x.GetOneDriveSyncStatusAsync("user1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncStatus);

        _stateManagerMock.Setup(x => x.CreateEscalationAsync(It.IsAny<ITEscalation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _orchestrator.ProcessUserTransitionAsync(migration, CancellationToken.None);

        // Assert
        _stateManagerMock.Verify(x => x.CreateEscalationAsync(
            It.Is<ITEscalation>(e => e.TriggerReason.Contains("Persistent OneDrive sync errors")),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}