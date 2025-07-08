using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using MigrationTool.Service.ProfileManagement.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class UserProfileManagerTests
{
    private readonly Mock<ILogger<UserProfileManager>> _loggerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<WindowsProfileDetector> _profileDetectorMock;
    private readonly Mock<IProfileActivityAnalyzer> _activityAnalyzerMock;
    private readonly Mock<ProfileClassifier> _profileClassifierMock;
    private readonly UserProfileManager _manager;

    public UserProfileManagerTests()
    {
        _loggerMock = new Mock<ILogger<UserProfileManager>>();
        _stateManagerMock = new Mock<IStateManager>();
        
        _profileDetectorMock = new Mock<WindowsProfileDetector>(
            new Mock<ILogger<WindowsProfileDetector>>().Object,
            new Mock<IWindowsProfileRegistry>().Object);
        
        _activityAnalyzerMock = new Mock<IProfileActivityAnalyzer>();
        
        _profileClassifierMock = new Mock<ProfileClassifier>(
            new Mock<ILogger<ProfileClassifier>>().Object,
            _activityAnalyzerMock.Object,
            new Mock<ClassificationOverrideManager>().Object);

        _manager = new UserProfileManager(
            _loggerMock.Object,
            _stateManagerMock.Object,
            _profileDetectorMock.Object,
            _activityAnalyzerMock.Object,
            _profileClassifierMock.Object);
    }

    [Fact]
    public async Task GetAllProfilesAsync_ReturnsFilteredProfiles_WhenSystemAccountsExcluded()
    {
        // Arrange
        var profiles = new List<UserProfile>
        {
            CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local),
            CreateProfile("S-1-5-18", "SYSTEM", ProfileType.Local),
            CreateProfile("S-1-5-21-1234-5678-9012-1002", "user2", ProfileType.Domain)
        };

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);

        // Act
        var result = await _manager.GetAllProfilesAsync(includeSystemAccounts: false);

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(p => p.UserId == "S-1-5-18");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1001");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1002");
    }

    [Fact]
    public async Task GetAllProfilesAsync_IncludesSystemAccounts_WhenRequested()
    {
        // Arrange
        var profiles = new List<UserProfile>
        {
            CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local),
            CreateProfile("S-1-5-18", "SYSTEM", ProfileType.Local)
        };

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);

        // Act
        var result = await _manager.GetAllProfilesAsync(includeSystemAccounts: true);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.UserId == "S-1-5-18");
    }

    [Fact]
    public async Task GetProfileAsync_ReturnsProfileFromState_WhenExists()
    {
        // Arrange
        var targetSid = "S-1-5-21-1234-5678-9012-1001";
        var profiles = new List<UserProfile>
        {
            CreateProfile(targetSid, "user1", ProfileType.Local),
            CreateProfile("S-1-5-21-1234-5678-9012-1002", "user2", ProfileType.Local)
        };

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);

        // Act
        var result = await _manager.GetProfileAsync(targetSid);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(targetSid);
        result.UserName.Should().Be("user1");
    }

    [Fact]
    public async Task GetProfileAsync_DiscoverFromWindows_WhenNotInState()
    {
        // Arrange
        var targetSid = "S-1-5-21-1234-5678-9012-1001";
        var newProfile = CreateProfile(targetSid, "user1", ProfileType.Local);
        var metrics = CreateMetrics();
        var classification = new ProfileClassificationResult
        {
            Classification = ProfileClassification.Active,
            RequiresBackup = true,
            BackupPriority = 100
        };

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile>());

        _profileDetectorMock
            .Setup(x => x.GetProfileBySidAsync(targetSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newProfile);

        _activityAnalyzerMock
            .Setup(x => x.AnalyzeProfileAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        _profileClassifierMock
            .Setup(x => x.ClassifyProfileAsync(It.IsAny<UserProfile>(), It.IsAny<ProfileMetrics>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(classification);

        // Act
        var result = await _manager.GetProfileAsync(targetSid);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(targetSid);
        result.IsActive.Should().BeTrue();
        result.RequiresBackup.Should().BeTrue();
        result.BackupPriority.Should().Be(100);
        
        _stateManagerMock.Verify(x => x.UpdateUserProfileAsync(It.Is<UserProfile>(p => p.UserId == targetSid), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task IsActiveUserAsync_ReturnsTrue_WhenProfileActive()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234", "user1", ProfileType.Local);
        var metrics = CreateMetrics();

        _activityAnalyzerMock
            .Setup(x => x.AnalyzeProfileAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        _activityAnalyzerMock
            .Setup(x => x.IsProfileActive(metrics))
            .Returns(true);

        // Act
        var result = await _manager.IsActiveUserAsync(profile);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CalculateMetricsAsync_ReturnsMetrics()
    {
        // Arrange
        var profile = CreateProfile("S-1-5-21-1234", "user1", ProfileType.Local);
        var expectedMetrics = CreateMetrics();

        _activityAnalyzerMock
            .Setup(x => x.AnalyzeProfileAsync(profile, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedMetrics);

        // Act
        var result = await _manager.CalculateMetricsAsync(profile);

        // Assert
        result.Should().BeSameAs(expectedMetrics);
    }

    [Fact]
    public async Task UpdateProfileStatusAsync_UpdatesExistingProfile()
    {
        // Arrange
        var targetSid = "S-1-5-21-1234-5678-9012-1001";
        var profile = CreateProfile(targetSid, "user1", ProfileType.Local);
        profile.IsActive = false;
        profile.RequiresBackup = false;

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile> { profile });

        // Act
        await _manager.UpdateProfileStatusAsync(targetSid, isActive: true, requiresBackup: true);

        // Assert
        _stateManagerMock.Verify(x => x.UpdateUserProfileAsync(
            It.Is<UserProfile>(p => 
                p.UserId == targetSid && 
                p.IsActive == true && 
                p.RequiresBackup == true), 
            It.IsAny<CancellationToken>()), 
            Times.Once);
    }

    [Fact]
    public async Task RefreshProfilesAsync_UpdatesAllProfiles()
    {
        // Arrange
        var windowsProfiles = new List<UserProfile>
        {
            CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local),
            CreateProfile("S-1-5-21-1234-5678-9012-1002", "user2", ProfileType.Domain)
        };

        var existingProfiles = new List<UserProfile>
        {
            CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local) // Existing
        };

        var metrics = CreateMetrics();
        var classification = new ProfileClassificationResult
        {
            Classification = ProfileClassification.Active,
            RequiresBackup = true,
            BackupPriority = 100
        };

        _profileDetectorMock
            .Setup(x => x.DiscoverProfilesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(windowsProfiles);

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingProfiles);

        _activityAnalyzerMock
            .Setup(x => x.AnalyzeProfileAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        _profileClassifierMock
            .Setup(x => x.ClassifyProfileAsync(It.IsAny<UserProfile>(), It.IsAny<ProfileMetrics>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(classification);

        // Act
        var result = await _manager.RefreshProfilesAsync();

        // Assert
        result.Should().Be(2); // Both profiles updated
        _stateManagerMock.Verify(x => x.UpdateUserProfileAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RefreshProfilesAsync_SkipsUnchangedProfiles()
    {
        // Arrange
        var existingProfile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        existingProfile.IsActive = true;
        existingProfile.RequiresBackup = true;
        existingProfile.BackupPriority = 100;
        existingProfile.ProfileSizeBytes = 1000 * 1024 * 1024;

        var discoveredProfile = CreateProfile("S-1-5-21-1234-5678-9012-1001", "user1", ProfileType.Local);
        discoveredProfile.IsActive = true;
        discoveredProfile.RequiresBackup = true;
        discoveredProfile.BackupPriority = 100;
        discoveredProfile.ProfileSizeBytes = 1000 * 1024 * 1024;

        var metrics = CreateMetrics();
        metrics.ProfileSizeBytes = 1000 * 1024 * 1024;
        
        var classification = new ProfileClassificationResult
        {
            Classification = ProfileClassification.Active,
            RequiresBackup = true,
            BackupPriority = 100
        };

        _profileDetectorMock
            .Setup(x => x.DiscoverProfilesAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile> { discoveredProfile });

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<UserProfile> { existingProfile });

        _activityAnalyzerMock
            .Setup(x => x.AnalyzeProfileAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(metrics);

        _profileClassifierMock
            .Setup(x => x.ClassifyProfileAsync(It.IsAny<UserProfile>(), It.IsAny<ProfileMetrics>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(classification);

        // Act
        var result = await _manager.RefreshProfilesAsync();

        // Assert
        result.Should().Be(0); // No updates needed
        _stateManagerMock.Verify(x => x.UpdateUserProfileAsync(It.IsAny<UserProfile>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetActiveProfilesRequiringBackupAsync_ReturnsFilteredAndSorted()
    {
        // Arrange
        var profile1 = CreateProfile("S-1-5-21-1001", "user1", ProfileType.Local);
        profile1.IsActive = true;
        profile1.RequiresBackup = true;
        profile1.BackupPriority = 50;

        var profile2 = CreateProfile("S-1-5-21-1002", "user2", ProfileType.Local);
        profile2.IsActive = false;
        profile2.RequiresBackup = true;
        profile2.BackupPriority = 100;

        var profile3 = CreateProfile("S-1-5-21-1003", "user3", ProfileType.Local);
        profile3.IsActive = true;
        profile3.RequiresBackup = false;
        profile3.BackupPriority = 200;

        var profile4 = CreateProfile("S-1-5-21-1004", "user4", ProfileType.Local);
        profile4.IsActive = true;
        profile4.RequiresBackup = true;
        profile4.BackupPriority = 150;

        var profile5 = CreateProfile("S-1-5-18", "SYSTEM", ProfileType.Local);
        profile5.IsActive = true;
        profile5.RequiresBackup = true;
        profile5.BackupPriority = 300;

        var profiles = new List<UserProfile> { profile1, profile2, profile3, profile4, profile5 };

        _stateManagerMock
            .Setup(x => x.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(profiles);

        // Act
        var result = await _manager.GetActiveProfilesRequiringBackupAsync();

        // Assert
        result.Should().HaveCount(2);
        result[0].UserId.Should().Be("S-1-5-21-1004"); // Higher priority first
        result[1].UserId.Should().Be("S-1-5-21-1001"); // Lower priority second
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

    private static ProfileMetrics CreateMetrics()
    {
        return new ProfileMetrics
        {
            IsAccessible = true,
            ProfileSizeBytes = 500 * 1024 * 1024,
            LastActivityTime = DateTime.UtcNow.AddDays(-5),
            LastLoginTime = DateTime.UtcNow.AddDays(-5),
            HasRecentActivity = true,
            IsLoaded = false,
            ActiveProcessCount = 0,
            Classification = ProfileClassification.Active
        };
    }
}