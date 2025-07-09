using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using Moq;
using Xunit;

namespace MigrationService.Tests;

[Collection("Database")]
public class EnhancedStateManagerTests : IDisposable
{
    private readonly Mock<ILogger<StateManager>> _loggerMock;
    private readonly Mock<IOptions<ServiceConfiguration>> _configMock;
    private readonly StateManager _stateManager;
    private readonly ServiceConfiguration _configuration;
    private readonly string _testDataPath;

    public EnhancedStateManagerTests()
    {
        _loggerMock = new Mock<ILogger<StateManager>>();
        _configMock = new Mock<IOptions<ServiceConfiguration>>();

        // Create a test directory
        _testDataPath = Path.Combine(Path.GetTempPath(), $"EnhancedStateManagerTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDataPath);

        _configuration = new ServiceConfiguration
        {
            DataPath = _testDataPath,
            LogPath = _testDataPath
        };

        _configMock.Setup(x => x.Value).Returns(_configuration);

        _stateManager = new StateManager(_loggerMock.Object, _configMock.Object);
    }

    public void Dispose()
    {
        _stateManager?.Dispose();

        if (Directory.Exists(_testDataPath))
        {
            try
            {
                Directory.Delete(_testDataPath, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    #region Backup Operation Tests

    [Fact]
    public async Task CreateBackupOperationAsync_ShouldCreateOperationWithUniqueId()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var operation = new BackupOperation
        {
            UserId = "test-user",
            ProviderName = "FileBackupProvider",
            Category = "files",
            Status = BackupStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            BytesTotal = 1000000
        };

        // Act
        var operationId = await _stateManager.CreateBackupOperationAsync(operation, CancellationToken.None);

        // Assert
        operationId.Should().NotBeNullOrEmpty();
        Guid.TryParse(operationId, out _).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateBackupOperationAsync_ShouldUpdateExistingOperation()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var operation = new BackupOperation
        {
            UserId = "test-user",
            ProviderName = "FileBackupProvider",
            Category = "files",
            Status = BackupStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            BytesTotal = 1000000
        };

        var operationId = await _stateManager.CreateBackupOperationAsync(operation, CancellationToken.None);

        // Update the operation
        operation.OperationId = operationId;
        operation.Status = BackupStatus.Completed;
        operation.Progress = 100;
        operation.BytesTransferred = 1000000;
        operation.CompletedAt = DateTime.UtcNow;

        // Act
        await _stateManager.UpdateBackupOperationAsync(operation, CancellationToken.None);
        var retrieved = await _stateManager.GetBackupOperationAsync(operationId, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Status.Should().Be(BackupStatus.Completed);
        retrieved.Progress.Should().Be(100);
        retrieved.BytesTransferred.Should().Be(1000000);
        retrieved.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserBackupOperationsAsync_ShouldReturnAllUserOperations()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var userId = "test-user";

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = userId,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        // Create multiple operations
        for (int i = 0; i < 3; i++)
        {
            var operation = new BackupOperation
            {
                UserId = userId,
                ProviderName = $"Provider{i}",
                Category = $"category{i}",
                Status = BackupStatus.Completed,
                StartedAt = DateTime.UtcNow.AddMinutes(-i),
                BytesTotal = 1000 * (i + 1)
            };

            await _stateManager.CreateBackupOperationAsync(operation, CancellationToken.None);
        }

        // Act
        var operations = await _stateManager.GetUserBackupOperationsAsync(userId, CancellationToken.None);

        // Assert
        operations.Should().HaveCount(3);
        operations.Should().BeInDescendingOrder(op => op.StartedAt);
    }

    [Fact]
    public async Task RecordProviderResultAsync_ShouldStoreProviderDetails()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var operation = new BackupOperation
        {
            UserId = "test-user",
            ProviderName = "FileBackupProvider",
            Category = "files",
            Status = BackupStatus.InProgress,
            StartedAt = DateTime.UtcNow,
            OperationId = Guid.NewGuid().ToString()
        };

        var operationId = await _stateManager.CreateBackupOperationAsync(operation, CancellationToken.None);

        // Get the saved operation to get the database ID
        var savedOperation = await _stateManager.GetBackupOperationAsync(operationId, CancellationToken.None);

        var result = new ProviderResult
        {
            BackupOperationId = savedOperation!.Id,
            Category = "files",
            Success = true,
            ItemCount = 150,
            SizeMB = 250,
            Duration = 300,
            Details = "Backed up Documents folder"
        };

        // Act & Assert
        var act = () => _stateManager.RecordProviderResultAsync(result, CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region OneDrive Sync Tests

    [Fact]
    public async Task UpdateOneDriveSyncStatusAsync_ShouldStoreStatus()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var status = new OneDriveSyncStatus
        {
            UserId = "test-user",
            IsInstalled = true,
            IsSignedIn = true,
            AccountEmail = "user@example.com",
            SyncFolderPath = @"C:\Users\TestUser\OneDrive",
            SyncStatus = "UpToDate",
            QuotaTotalMB = 5120,
            QuotaUsedMB = 2048,
            QuotaAvailableMB = 3072,
            LastSyncTime = DateTime.UtcNow,
            ErrorCount = 0
        };

        // Act
        await _stateManager.UpdateOneDriveSyncStatusAsync(status, CancellationToken.None);
        var retrieved = await _stateManager.GetOneDriveSyncStatusAsync(status.UserId, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.IsInstalled.Should().BeTrue();
        retrieved.IsSignedIn.Should().BeTrue();
        retrieved.AccountEmail.Should().Be("user@example.com");
        retrieved.QuotaAvailableMB.Should().Be(3072);
    }

    [Fact]
    public async Task GetUsersWithSyncErrorsAsync_ShouldReturnUsersWithErrors()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profiles to satisfy foreign key constraints
        var errorUserProfile = new UserProfile
        {
            UserId = "error-user",
            UserName = "erroruser",
            ProfilePath = @"C:\Users\erroruser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(errorUserProfile, CancellationToken.None);

        var goodUserProfile = new UserProfile
        {
            UserId = "good-user",
            UserName = "gooduser",
            ProfilePath = @"C:\Users\gooduser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(goodUserProfile, CancellationToken.None);

        // User with errors
        await _stateManager.UpdateOneDriveSyncStatusAsync(new OneDriveSyncStatus
        {
            UserId = "error-user",
            IsInstalled = true,
            IsSignedIn = true,
            SyncStatus = "Error",
            ErrorCount = 5,
            LastSyncError = "Quota exceeded"
        }, CancellationToken.None);

        // User without errors
        await _stateManager.UpdateOneDriveSyncStatusAsync(new OneDriveSyncStatus
        {
            UserId = "good-user",
            IsInstalled = true,
            IsSignedIn = true,
            SyncStatus = "UpToDate",
            ErrorCount = 0
        }, CancellationToken.None);

        // Act
        var usersWithErrors = await _stateManager.GetUsersWithSyncErrorsAsync(CancellationToken.None);

        // Assert
        usersWithErrors.Should().ContainSingle();
        usersWithErrors.First().UserId.Should().Be("error-user");
    }

    #endregion

    #region IT Escalation Tests

    [Fact]
    public async Task CreateEscalationAsync_ShouldCreateAndReturnId()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var escalation = new ITEscalation
        {
            UserId = "test-user",
            TriggerType = EscalationTriggerType.QuotaExceeded,
            TriggerReason = "OneDrive quota insufficient",
            Details = "User needs 5GB but only has 1GB available",
            AutoTriggered = true
        };

        // Act
        var escalationId = await _stateManager.CreateEscalationAsync(escalation, CancellationToken.None);

        // Assert
        escalationId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UpdateEscalationAsync_ShouldUpdateStatus()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var escalation = new ITEscalation
        {
            UserId = "test-user",
            TriggerType = EscalationTriggerType.SyncError,
            TriggerReason = "Persistent sync failures"
        };

        var id = await _stateManager.CreateEscalationAsync(escalation, CancellationToken.None);

        // Update escalation
        escalation.Id = id;
        escalation.Status = "Resolved";
        escalation.TicketNumber = "INC0012345";
        escalation.ResolvedAt = DateTime.UtcNow;
        escalation.ResolutionNotes = "Increased user quota";

        // Act
        await _stateManager.UpdateEscalationAsync(escalation, CancellationToken.None);
        var openEscalations = await _stateManager.GetOpenEscalationsAsync(CancellationToken.None);

        // Assert
        openEscalations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserEscalationsAsync_ShouldReturnUserSpecificEscalations()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var userId = "test-user";

        // First create the user profiles to satisfy foreign key constraints
        var testUserProfile = new UserProfile
        {
            UserId = userId,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(testUserProfile, CancellationToken.None);

        var otherUserProfile = new UserProfile
        {
            UserId = "other-user",
            UserName = "otheruser",
            ProfilePath = @"C:\Users\otheruser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(otherUserProfile, CancellationToken.None);

        // Create escalations for test user
        await _stateManager.CreateEscalationAsync(new ITEscalation
        {
            UserId = userId,
            TriggerType = EscalationTriggerType.QuotaExceeded,
            TriggerReason = "Quota issue"
        }, CancellationToken.None);

        // Create escalation for different user
        await _stateManager.CreateEscalationAsync(new ITEscalation
        {
            UserId = "other-user",
            TriggerType = EscalationTriggerType.BackupFailure,
            TriggerReason = "Backup failed"
        }, CancellationToken.None);

        // Act
        var userEscalations = await _stateManager.GetUserEscalationsAsync(userId, CancellationToken.None);

        // Assert
        userEscalations.Should().ContainSingle();
        userEscalations.First().UserId.Should().Be(userId);
    }

    #endregion

    #region Delay Request Tests

    [Fact]
    public async Task CreateDelayRequestAsync_ShouldCreateRequest()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        var request = new DelayRequest
        {
            UserId = "test-user",
            RequestedDelayHours = 24,
            Reason = "User traveling",
            Status = "Pending"
        };

        // Act
        var requestId = await _stateManager.CreateDelayRequestAsync(request, CancellationToken.None);

        // Assert
        requestId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ApproveDelayRequestAsync_ShouldUpdateRequestAndMigrationState()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Create user profile and migration state
        await _stateManager.UpdateUserProfileAsync(new UserProfile
        {
            UserId = "test-user",
            UserName = "Test User",
            ProfilePath = @"C:\Users\TestUser",
            LastLoginTime = DateTime.UtcNow,
            IsActive = true,
            ProfileSizeBytes = 100
        }, CancellationToken.None);

        await _stateManager.UpdateMigrationStateAsync(new MigrationState
        {
            UserId = "test-user",
            State = MigrationStateType.WaitingForUser,
            Deadline = DateTime.UtcNow.AddDays(1)
        }, CancellationToken.None);

        // Create delay request
        var request = new DelayRequest
        {
            UserId = "test-user",
            RequestedDelayHours = 24,
            Reason = "User needs more time"
        };

        var requestId = await _stateManager.CreateDelayRequestAsync(request, CancellationToken.None);
        var newDeadline = DateTime.UtcNow.AddDays(2);

        // Act
        await _stateManager.ApproveDelayRequestAsync(requestId, newDeadline, CancellationToken.None);

        var migrationState = await _stateManager.GetMigrationStateAsync("test-user", CancellationToken.None);
        var pendingRequests = await _stateManager.GetPendingDelayRequestsAsync(CancellationToken.None);

        // Assert
        migrationState!.Deadline.Should().BeCloseTo(newDeadline, TimeSpan.FromSeconds(1));
        migrationState.DelayCount.Should().Be(1);
        pendingRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserDelayCountAsync_ShouldReturnApprovedCount()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var userId = "test-user";

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = userId,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        // Create and approve multiple delay requests
        for (int i = 0; i < 2; i++)
        {
            var requestId = await _stateManager.CreateDelayRequestAsync(new DelayRequest
            {
                UserId = userId,
                RequestedDelayHours = 24,
                Reason = $"Reason {i}"
            }, CancellationToken.None);

            await _stateManager.ApproveDelayRequestAsync(
                requestId, DateTime.UtcNow.AddDays(i + 2), CancellationToken.None);
        }

        // Create pending request (should not be counted)
        await _stateManager.CreateDelayRequestAsync(new DelayRequest
        {
            UserId = userId,
            RequestedDelayHours = 24,
            Reason = "Pending request"
        }, CancellationToken.None);

        // Act
        var count = await _stateManager.GetUserDelayCountAsync(userId, CancellationToken.None);

        // Assert
        count.Should().Be(2);
    }

    #endregion

    #region State History Tests

    [Fact]
    public async Task TransitionStateAsync_ShouldRecordStateHistory()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var userId = "test-user";

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = userId,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        // Create initial state
        await _stateManager.UpdateMigrationStateAsync(new MigrationState
        {
            UserId = userId,
            State = MigrationStateType.NotStarted
        }, CancellationToken.None);

        // Act
        await _stateManager.TransitionStateAsync(
            userId, MigrationStateType.Initializing, "Starting migration", CancellationToken.None);

        var history = await _stateManager.GetStateHistoryAsync(userId, CancellationToken.None);

        // Assert
        history.Should().ContainSingle();
        var entry = history.First();
        entry.OldState.Should().Be("NotStarted");
        entry.NewState.Should().Be("Initializing");
        entry.Reason.Should().Be("Starting migration");
    }

    [Fact]
    public async Task RecordSystemEventAsync_ShouldStoreEvent()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Act & Assert
        var act = () => _stateManager.RecordSystemEventAsync(
            "TestEvent", "Information", "Test message", "Test details",
            "test-user", CancellationToken.None);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Aggregated Query Tests

    [Fact]
    public async Task GetMigrationReadinessAsync_ShouldCalculateCorrectStatus()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Create users with different states
        var users = new[]
        {
            ("user1", true, MigrationStateType.BackupCompleted),
            ("user2", true, MigrationStateType.ReadyForReset),
            ("user3", true, MigrationStateType.BackupInProgress),
            ("user4", false, MigrationStateType.NotStarted) // Inactive user
        };

        foreach (var (userId, isActive, state) in users)
        {
            await _stateManager.UpdateUserProfileAsync(new UserProfile
            {
                UserId = userId,
                UserName = userId,
                ProfilePath = $@"C:\Users\{userId}",
                LastLoginTime = DateTime.UtcNow,
                IsActive = isActive,
                RequiresBackup = true,
                ProfileSizeBytes = 100
            }, CancellationToken.None);

            if (isActive)
            {
                await _stateManager.UpdateMigrationStateAsync(new MigrationState
                {
                    UserId = userId,
                    State = state,
                    IsBlocking = state != MigrationStateType.BackupCompleted &&
                                state != MigrationStateType.ReadyForReset
                }, CancellationToken.None);
            }
        }

        // Act
        var readiness = await _stateManager.GetMigrationReadinessAsync(CancellationToken.None);

        // Assert
        readiness.TotalUsers.Should().Be(4);
        readiness.ActiveUsers.Should().Be(3);
        readiness.CompletedUsers.Should().Be(2);
        readiness.BlockingUsers.Should().Be(1);
        readiness.CanReset.Should().BeFalse();
        readiness.BlockingUserNames.Should().Contain("user3");
    }

    [Fact]
    public async Task GetUsersRequiringAttentionAsync_ShouldIdentifyProblematicUsers()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Create user profiles first to satisfy foreign key constraints
        var attentionUserProfile = new UserProfile
        {
            UserId = "attention-user",
            UserName = "attentionuser",
            ProfilePath = @"C:\Users\attentionuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(attentionUserProfile, CancellationToken.None);

        var syncErrorUserProfile = new UserProfile
        {
            UserId = "sync-error-user",
            UserName = "syncerroruser",
            ProfilePath = @"C:\Users\syncerroruser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(syncErrorUserProfile, CancellationToken.None);

        var normalUserProfile = new UserProfile
        {
            UserId = "normal-user",
            UserName = "normaluser",
            ProfilePath = @"C:\Users\normaluser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(normalUserProfile, CancellationToken.None);

        // User with attention reason
        await _stateManager.UpdateMigrationStateAsync(new MigrationState
        {
            UserId = "attention-user",
            State = MigrationStateType.Failed,
            AttentionReason = "Backup failed repeatedly"
        }, CancellationToken.None);

        // User with sync errors
        await _stateManager.UpdateOneDriveSyncStatusAsync(new OneDriveSyncStatus
        {
            UserId = "sync-error-user",
            ErrorCount = 3,
            SyncStatus = "Error"
        }, CancellationToken.None);

        // Normal user
        await _stateManager.UpdateMigrationStateAsync(new MigrationState
        {
            UserId = "normal-user",
            State = MigrationStateType.BackupInProgress
        }, CancellationToken.None);

        // Act
        var usersNeedingAttention = await _stateManager.GetUsersRequiringAttentionAsync(CancellationToken.None);

        // Assert
        usersNeedingAttention.Should().HaveCount(2);
        usersNeedingAttention.Should().Contain("attention-user");
        usersNeedingAttention.Should().Contain("sync-error-user");
    }

    #endregion

    #region Maintenance Operation Tests

    [Fact]
    public async Task CleanupStaleOperationsAsync_ShouldMarkStaleOperationsAsFailed()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create the user profile to satisfy foreign key constraint
        var userProfile = new UserProfile
        {
            UserId = "test-user",
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _stateManager.UpdateUserProfileAsync(userProfile, CancellationToken.None);

        // Create a stale operation
        var operation = new BackupOperation
        {
            UserId = "test-user",
            ProviderName = "FileBackupProvider",
            Category = "files",
            Status = BackupStatus.InProgress,
            StartedAt = DateTime.UtcNow.AddHours(-25) // Over 24 hours old
        };

        await _stateManager.CreateBackupOperationAsync(operation, CancellationToken.None);

        // Act
        await _stateManager.CleanupStaleOperationsAsync(TimeSpan.FromHours(24), CancellationToken.None);

        // Assert
        var operations = await _stateManager.GetUserBackupOperationsAsync("test-user", CancellationToken.None);
        operations.First().Status.Should().Be(BackupStatus.Failed);
        operations.First().ErrorCode.Should().Be("TIMEOUT");
    }

    [Fact]
    public async Task GetDatabaseStatisticsAsync_ShouldReturnStats()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Add some data
        await _stateManager.UpdateUserProfileAsync(new UserProfile
        {
            UserId = "test-user",
            UserName = "Test User",
            ProfilePath = @"C:\Users\TestUser",
            LastLoginTime = DateTime.UtcNow,
            IsActive = true,
            ProfileSizeBytes = 100
        }, CancellationToken.None);

        // Act
        var stats = await _stateManager.GetDatabaseStatisticsAsync(CancellationToken.None);

        // Assert
        stats.Should().ContainKey("UserProfiles_Count");
        stats["UserProfiles_Count"].Should().Be(1);
        stats.Should().ContainKey("DatabaseSizeMB");
    }

    #endregion
}
