using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class ProfileClassifierTests
{
    private readonly Mock<ILogger<ProfileClassifier>> _loggerMock;
    private readonly Mock<ProfileActivityAnalyzer> _activityAnalyzerMock;
    private readonly ProfileClassifier _classifier;
    private readonly ProfileClassificationConfig _config;

    public ProfileClassifierTests()
    {
        _loggerMock = new Mock<ILogger<ProfileClassifier>>();
        _activityAnalyzerMock = new Mock<ProfileActivityAnalyzer>(
            new Mock<ILogger<ProfileActivityAnalyzer>>().Object,
            null,
            null);

        _config = new ProfileClassificationConfig
        {
            ActiveThreshold = TimeSpan.FromDays(30),
            InactiveThreshold = TimeSpan.FromDays(90),
            MinimumActiveSizeMB = 100,
            MinimumBackupSizeMB = 50,
            BackupInactiveProfiles = true
        };

        _classifier = new ProfileClassifier(_loggerMock.Object, _activityAnalyzerMock.Object, _config);
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsSystem_ForSystemAccount()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-18", "SYSTEM", ProfileType.Local);
        var metrics = CreateMetrics(isAccessible: true, sizeMB: 500);

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.System);
        result.RequiresBackup.Should().BeFalse();
        result.BackupPriority.Should().Be(0);
        result.Reason.Should().Contain("System or service account");
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsTemporary_ForTemporaryProfile()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        profile.ProfilePath = @"C:\Users\user1.TEMP";
        var metrics = CreateMetrics(isAccessible: true, sizeMB: 500);

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Temporary);
        result.RequiresBackup.Should().BeFalse();
        result.BackupPriority.Should().Be(0);
        result.Reason.Should().Contain("Temporary profile");
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsCorrupted_WhenNotAccessible()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(isAccessible: false, sizeMB: 500);
        metrics.Errors.Add("Access denied");

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Corrupted);
        result.RequiresBackup.Should().BeFalse();
        result.BackupPriority.Should().Be(0);
        result.Reason.Should().Contain("corrupted or inaccessible");
        result.Errors.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsActive_ForCurrentlyLoadedProfile()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 200,
            isLoaded: true,
            activeProcessCount: 5,
            lastActivity: DateTime.UtcNow.AddDays(-5));

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Active);
        result.RequiresBackup.Should().BeTrue();
        result.BackupPriority.Should().BeGreaterThan(100); // High priority for loaded profile
        result.Reason.Should().Contain("Active user");
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsActive_ForRecentActivity()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 200,
            lastActivity: DateTime.UtcNow.AddDays(-10)); // Within 30 day threshold

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Active);
        result.RequiresBackup.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsInactive_ForOldActivity()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 200,
            lastActivity: DateTime.UtcNow.AddDays(-120)); // Beyond 90 day threshold

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Inactive);
        result.RequiresBackup.Should().BeTrue(); // Still backed up based on config
        result.BackupPriority.Should().BeLessThan(100); // Lower priority than active
        result.Reason.Should().Contain("Inactive user");
    }

    [Fact]
    public async Task ClassifyProfileAsync_ReturnsInactive_ForSmallProfile()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 50, // Below 100MB threshold
            lastActivity: DateTime.UtcNow.AddDays(-5));

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Inactive);
        result.RequiresBackup.Should().BeTrue(); // 50MB meets minimum backup size
    }

    [Fact]
    public async Task ClassifyProfileAsync_DoesNotBackupInactive_WhenDisabled()
    {
        // Arrange
        var config = new ProfileClassificationConfig { BackupInactiveProfiles = false };
        var classifier = new ProfileClassifier(_loggerMock.Object, _activityAnalyzerMock.Object, config);
        
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 200,
            lastActivity: DateTime.UtcNow.AddDays(-120));

        // Act
        var result = await classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.Classification.Should().Be(ProfileClassification.Inactive);
        result.RequiresBackup.Should().BeFalse();
        result.BackupPriority.Should().Be(0);
    }

    [Fact]
    public async Task ClassifyProfileAsync_CalculatesPriorityCorrectly_ForLargeActiveProfile()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Domain);
        var metrics = CreateMetrics(
            isAccessible: true,
            sizeMB: 15000, // 15GB
            isLoaded: true,
            activeProcessCount: 10,
            lastActivity: DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        result.Should().NotBeNull();
        result.BackupPriority.Should().BeGreaterThan(150); // High priority
        // Base (100) + Size>10GB (20) + Activity<1day (20) + Loaded (30) + Domain (10) = 180+
    }

    [Theory]
    [InlineData("S-1-5-80-1234", true)]  // Service account
    [InlineData("S-1-5-82-1234", true)]  // IIS AppPool
    [InlineData("S-1-5-19", true)]       // LOCAL_SERVICE
    [InlineData("S-1-5-20", true)]       // NETWORK_SERVICE
    [InlineData("S-1-5-21-1234-5678-9012-1001", false)] // Regular user
    public async Task ClassifyProfileAsync_IdentifiesSystemAccounts(string sid, bool expectedSystem)
    {
        // Arrange
        var profile = CreateProfile(sid, "testuser", ProfileType.Local);
        var metrics = CreateMetrics(isAccessible: true, sizeMB: 200);

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        if (expectedSystem)
        {
            result.Classification.Should().Be(ProfileClassification.System);
            result.RequiresBackup.Should().BeFalse();
        }
        else
        {
            result.Classification.Should().NotBe(ProfileClassification.System);
        }
    }

    [Theory]
    [InlineData("user$", true)]
    [InlineData("IUSR", true)]
    [InlineData("ASPNET", true)]
    [InlineData("NT SERVICE\\Something", true)]
    [InlineData("john.doe", false)]
    public async Task ClassifyProfileAsync_IdentifiesSystemAccountsByName(string userName, bool expectedSystem)
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234-5678-9012-1001", userName, ProfileType.Local);
        var metrics = CreateMetrics(isAccessible: true, sizeMB: 200);

        // Act
        var result = await _classifier.ClassifyProfileAsync(profile, metrics);

        // Assert
        if (expectedSystem)
        {
            result.Classification.Should().Be(ProfileClassification.System);
            result.RequiresBackup.Should().BeFalse();
        }
        else
        {
            result.Classification.Should().NotBe(ProfileClassification.System);
        }
    }

    private static UserProfile CreateProfile(string sid, string userName, ProfileType profileType)
    {
        return new UserProfile
        {
            UserId = sid,
            UserName = userName,
            ProfilePath = $@"C:\Users\{userName}",
            ProfileType = profileType,
            LastLoginTime = DateTime.UtcNow.AddDays(-10),
            IsActive = false,
            ProfileSizeBytes = 0,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static ProfileMetrics CreateMetrics(
        bool isAccessible = true,
        long sizeMB = 100,
        bool isLoaded = false,
        int activeProcessCount = 0,
        DateTime? lastActivity = null)
    {
        var metrics = new ProfileMetrics
        {
            IsAccessible = isAccessible,
            ProfileSizeBytes = sizeMB * 1024 * 1024,
            IsLoaded = isLoaded,
            ActiveProcessCount = activeProcessCount,
            LastActivityTime = lastActivity ?? DateTime.UtcNow.AddDays(-45),
            LastLoginTime = lastActivity ?? DateTime.UtcNow.AddDays(-45),
            HasRecentActivity = (DateTime.UtcNow - (lastActivity ?? DateTime.UtcNow.AddDays(-45))).TotalDays <= 30,
            Classification = isAccessible ? ProfileClassification.Unknown : ProfileClassification.Corrupted
        };

        if (!isAccessible)
        {
            metrics.Errors.Add("Profile directory not accessible");
        }

        return metrics;
    }
}