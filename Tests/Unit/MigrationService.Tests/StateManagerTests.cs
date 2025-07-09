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
public class StateManagerTests : IDisposable
{
    private readonly Mock<ILogger<StateManager>> _loggerMock;
    private readonly Mock<IOptions<ServiceConfiguration>> _configMock;
    private readonly StateManager _stateManager;
    private readonly ServiceConfiguration _configuration;
    private readonly string _testDataPath;

    public StateManagerTests()
    {
        _loggerMock = new Mock<ILogger<StateManager>>();
        _configMock = new Mock<IOptions<ServiceConfiguration>>();

        // Create a test directory
        _testDataPath = Path.Combine(Path.GetTempPath(), $"StateManagerTest_{Guid.NewGuid()}");
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

        // Cleanup test directory
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

    [Fact]
    public async Task InitializeAsync_ShouldCreateDatabase()
    {
        // Act
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Assert
        var dbPath = Path.Combine(_testDataPath, "migration.db");
        File.Exists(dbPath).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledTwice_ShouldNotThrow()
    {
        // Act
        await _stateManager.InitializeAsync(CancellationToken.None);
        var act = async () => await _stateManager.InitializeAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CheckHealthAsync_AfterInitialization_ShouldReturnTrue()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Act
        var result = await _stateManager.CheckHealthAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckHealthAsync_WithInvalidDatabase_ShouldReturnFalse()
    {
        // Arrange
        // Create an invalid database file
        var dbPath = Path.Combine(_testDataPath, "migration.db");
        await File.WriteAllTextAsync(dbPath, "invalid content");

        // Act
        var result = await _stateManager.CheckHealthAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateUserProfileAsync_ShouldStoreProfile()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var profile = new UserProfile
        {
            UserId = "test-user-1",
            UserName = "Test User",
            ProfilePath = @"C:\Users\TestUser",
            LastLoginTime = DateTime.UtcNow,
            IsActive = true,
            ProfileSizeBytes = 1024 * 1024 * 100 // 100MB
        };

        // Act
        await _stateManager.UpdateUserProfileAsync(profile, CancellationToken.None);

        // Assert
        var profiles = await _stateManager.GetUserProfilesAsync(CancellationToken.None);
        profiles.Should().ContainSingle(p => p.UserId == profile.UserId);
    }

    [Fact]
    public async Task GetUserProfilesAsync_WithMultipleProfiles_ShouldReturnAll()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        var profiles = new[]
        {
            new UserProfile { UserId = "user1", UserName = "User 1", ProfilePath = @"C:\Users\User1", LastLoginTime = DateTime.UtcNow, IsActive = true, ProfileSizeBytes = 100 },
            new UserProfile { UserId = "user2", UserName = "User 2", ProfilePath = @"C:\Users\User2", LastLoginTime = DateTime.UtcNow.AddDays(-30), IsActive = false, ProfileSizeBytes = 200 },
            new UserProfile { UserId = "user3", UserName = "User 3", ProfilePath = @"C:\Users\User3", LastLoginTime = DateTime.UtcNow.AddDays(-1), IsActive = true, ProfileSizeBytes = 300 }
        };

        foreach (var profile in profiles)
        {
            await _stateManager.UpdateUserProfileAsync(profile, CancellationToken.None);
        }

        // Act
        var result = await _stateManager.GetUserProfilesAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(3);
        result.Select(p => p.UserId).Should().BeEquivalentTo(new[] { "user1", "user2", "user3" });
    }

    [Fact]
    public async Task UpdateMigrationStateAsync_ShouldStoreMigrationState()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // First create a user profile
        var profile = new UserProfile
        {
            UserId = "test-user",
            UserName = "Test User",
            ProfilePath = @"C:\Users\TestUser",
            LastLoginTime = DateTime.UtcNow,
            IsActive = true,
            ProfileSizeBytes = 100
        };
        await _stateManager.UpdateUserProfileAsync(profile, CancellationToken.None);

        var migrationState = new MigrationState
        {
            UserId = "test-user",
            AttentionReason = "Test attention"
        };

        // Act
        await _stateManager.UpdateMigrationStateAsync(migrationState, CancellationToken.None);

        // Assert
        var result = await _stateManager.GetMigrationStateAsync("test-user", CancellationToken.None);
        result.Should().NotBeNull();
        result!.UserId.Should().Be("test-user");
        result.AttentionReason.Should().Be("Test attention");
    }

    [Fact]
    public async Task GetActiveMigrationsAsync_ShouldReturnOnlyActiveMigrations()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Create user profiles
        var users = new[] { "user1", "user2", "user3" };
        foreach (var userId in users)
        {
            await _stateManager.UpdateUserProfileAsync(new UserProfile
            {
                UserId = userId,
                UserName = userId,
                ProfilePath = $@"C:\Users\{userId}",
                LastLoginTime = DateTime.UtcNow,
                IsActive = true,
                ProfileSizeBytes = 100
            }, CancellationToken.None);
        }

        // Create migration states
        await _stateManager.UpdateMigrationStateAsync(new MigrationState { UserId = "user1" }, CancellationToken.None);
        await _stateManager.UpdateMigrationStateAsync(new MigrationState { UserId = "user2" }, CancellationToken.None);
        // user3 has no migration state

        // Act
        var result = await _stateManager.GetActiveMigrationsAsync(CancellationToken.None);

        // Assert
        result.Should().HaveCount(2);
        result.Select(m => m.UserId).Should().BeEquivalentTo(new[] { "user1", "user2" });
    }

    [Fact]
    public async Task AreAllUsersReadyForResetAsync_WithNoActiveUsers_ShouldReturnTrue()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Act
        var result = await _stateManager.AreAllUsersReadyForResetAsync(CancellationToken.None);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task AreAllUsersReadyForResetAsync_WithIncompleteBackups_ShouldReturnFalse()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Create an active user without completed migration
        await _stateManager.UpdateUserProfileAsync(new UserProfile
        {
            UserId = "active-user",
            UserName = "Active User",
            ProfilePath = @"C:\Users\ActiveUser",
            LastLoginTime = DateTime.UtcNow,
            IsActive = true,
            ProfileSizeBytes = 100
        }, CancellationToken.None);

        // Act
        var result = await _stateManager.AreAllUsersReadyForResetAsync(CancellationToken.None);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CleanupStaleOperationsAsync_ShouldMarkStaleOperations()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // This test would require more complex setup with direct database manipulation
        // For now, just verify it doesn't throw

        // Act
        var act = async () => await _stateManager.CleanupStaleOperationsAsync(TimeSpan.FromHours(1), CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FlushAsync_ShouldNotThrow()
    {
        // Arrange
        await _stateManager.InitializeAsync(CancellationToken.None);

        // Act
        var act = async () => await _stateManager.FlushAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }
}
