using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class QuotaWarningManagerTests
{
    private readonly Mock<ILogger<QuotaWarningManager>> _loggerMock;
    private readonly Mock<IOneDriveQuotaChecker> _quotaCheckerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IUserProfileManager> _profileManagerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly QuotaWarningManager _warningManager;

    public QuotaWarningManagerTests()
    {
        _loggerMock = new Mock<ILogger<QuotaWarningManager>>();
        _quotaCheckerMock = new Mock<IOneDriveQuotaChecker>();
        _stateManagerMock = new Mock<IStateManager>();
        _profileManagerMock = new Mock<IUserProfileManager>();
        _configurationMock = new Mock<IConfiguration>();

        SetupConfiguration();

        _warningManager = new QuotaWarningManager(
            _loggerMock.Object,
            _quotaCheckerMock.Object,
            _stateManagerMock.Object,
            _profileManagerMock.Object,
            _configurationMock.Object);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithGoodQuotaHealth_DoesNotCreateWarning()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Good, 20.0, canAccommodateBackup: true);

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithHighUsage_CreatesHighUsageWarning()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Warning, 85.0, canAccommodateBackup: true);

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        _stateManagerMock.Setup(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(
            It.Is<QuotaWarning>(w => w.WarningType == QuotaWarningType.HighUsage && w.Level == QuotaWarningLevel.Warning),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithInsufficientBackupSpace_CreatesCriticalWarning()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Critical, 75.0, canAccommodateBackup: false);

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        _stateManagerMock.Setup(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(
            It.Is<QuotaWarning>(w => w.WarningType == QuotaWarningType.InsufficientBackupSpace && w.Level == QuotaWarningLevel.Critical),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithExistingUnresolvedWarning_DoesNotCreateDuplicate()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Warning, 85.0, canAccommodateBackup: true);

        var existingWarning = new QuotaWarning
        {
            Id = 1,
            UserId = userSid,
            WarningType = QuotaWarningType.HighUsage,
            Level = QuotaWarningLevel.Warning,
            IsResolved = false,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning> { existingWarning });

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithResolvedWarningInCooldown_DoesNotCreateNew()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Warning, 85.0, canAccommodateBackup: true);

        var recentResolvedWarning = new QuotaWarning
        {
            Id = 1,
            UserId = userSid,
            WarningType = QuotaWarningType.HighUsage,
            Level = QuotaWarningLevel.Warning,
            IsResolved = true,
            ResolvedAt = DateTime.UtcNow.AddHours(-12), // Within 24-hour cooldown
            CreatedAt = DateTime.UtcNow.AddHours(-13)
        };

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        _stateManagerMock.Setup(x => x.GetQuotaWarningsByTypeAsync(QuotaWarningType.HighUsage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning> { recentResolvedWarning });

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithMultipleRepeatedWarnings_CreatesEscalation()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(QuotaHealthLevel.Warning, 85.0, canAccommodateBackup: true);

        var repeatedWarnings = new List<QuotaWarning>();
        for (int i = 0; i < 3; i++)
        {
            repeatedWarnings.Add(new QuotaWarning
            {
                Id = i + 1,
                UserId = userSid,
                WarningType = QuotaWarningType.HighUsage,
                Level = QuotaWarningLevel.Warning,
                IsResolved = true,
                CreatedAt = DateTime.UtcNow.AddDays(-i - 1),
                ResolvedAt = DateTime.UtcNow.AddDays(-i - 1).AddHours(1)
            });
        }

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        _stateManagerMock.Setup(x => x.GetQuotaWarningsByTypeAsync(QuotaWarningType.HighUsage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repeatedWarnings);

        _stateManagerMock.Setup(x => x.CreateQuotaEscalationAsync(It.IsAny<QuotaEscalation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaEscalationAsync(
            It.Is<QuotaEscalation>(e => e.EscalationType == QuotaEscalationType.RepeatedWarnings),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessAllUsersWarningsAsync_WithMultipleUsers_ProcessesAll()
    {
        // Arrange
        var users = new List<UserProfile>
        {
            new UserProfile { UserId = "user1", IsActive = true },
            new UserProfile { UserId = "user2", IsActive = true },
            new UserProfile { UserId = "user3", IsActive = false } // Inactive user should be skipped
        };

        _profileManagerMock.Setup(x => x.GetActiveProfilesRequiringBackupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(users.Where(u => u.IsActive).ToList());

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateQuotaStatus(QuotaHealthLevel.Good, 20.0, canAccommodateBackup: true));

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        // Act
        await _warningManager.ProcessAllUsersWarningsAsync();

        // Assert
        _quotaCheckerMock.Verify(x => x.CheckQuotaHealthAsync("user1", It.IsAny<CancellationToken>()), Times.Once);
        _quotaCheckerMock.Verify(x => x.CheckQuotaHealthAsync("user2", It.IsAny<CancellationToken>()), Times.Once);
        _quotaCheckerMock.Verify(x => x.CheckQuotaHealthAsync("user3", It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProcessUserWarningsAsync_WithNullQuotaStatus_DoesNotCreateWarning()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuotaStatus?)null);

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
            Times.Never);
        _stateManagerMock.Verify(x => x.CreateQuotaEscalationAsync(It.IsAny<QuotaEscalation>(), It.IsAny<CancellationToken>()), 
            Times.Never);
    }

    [Theory]
    [InlineData(QuotaHealthLevel.Good, 20.0, false)]
    [InlineData(QuotaHealthLevel.Warning, 85.0, true)]
    [InlineData(QuotaHealthLevel.Critical, 96.0, true)]
    public async Task ProcessUserWarningsAsync_WithVariousHealthLevels_CreatesWarningsAppropriately(
        QuotaHealthLevel healthLevel, double usagePercentage, bool shouldCreateWarning)
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var quotaStatus = CreateQuotaStatus(healthLevel, usagePercentage, canAccommodateBackup: true);

        _quotaCheckerMock.Setup(x => x.CheckQuotaHealthAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(quotaStatus);

        _stateManagerMock.Setup(x => x.GetUnresolvedQuotaWarningsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<QuotaWarning>());

        _stateManagerMock.Setup(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        // Act
        await _warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        if (shouldCreateWarning)
        {
            _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
                Times.AtLeastOnce);
        }
        else
        {
            _stateManagerMock.Verify(x => x.CreateQuotaWarningAsync(It.IsAny<QuotaWarning>(), It.IsAny<CancellationToken>()), 
                Times.Never);
        }
    }

    private QuotaStatus CreateQuotaStatus(QuotaHealthLevel healthLevel, double usagePercentage, bool canAccommodateBackup)
    {
        const long totalSpace = 10000;
        var usedSpace = (long)(totalSpace * usagePercentage / 100);

        return new QuotaStatus
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            TotalSpaceMB = totalSpace,
            UsedSpaceMB = usedSpace,
            AvailableSpaceMB = totalSpace - usedSpace,
            RequiredSpaceMB = canAccommodateBackup ? 100 : 5000,
            HealthLevel = healthLevel,
            UsagePercentage = usagePercentage,
            CanAccommodateBackup = canAccommodateBackup,
            ShortfallMB = canAccommodateBackup ? 0 : 1000,
            Issues = healthLevel != QuotaHealthLevel.Good ? $"{healthLevel} quota issue detected" : null,
            Recommendations = healthLevel != QuotaHealthLevel.Good ? "Consider cleaning up files or requesting more quota" : null,
            LastChecked = DateTime.UtcNow
        };
    }

    private void SetupConfiguration()
    {
        var autoEscalation = new Mock<IConfigurationSection>();
        autoEscalation.Setup(x => x.Value).Returns("true");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:AutoEscalationEnabled"))
            .Returns(autoEscalation.Object);

        var warningCooldown = new Mock<IConfigurationSection>();
        warningCooldown.Setup(x => x.Value).Returns("24");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:WarningCooldownHours"))
            .Returns(warningCooldown.Object);

        var escalationCooldown = new Mock<IConfigurationSection>();
        escalationCooldown.Setup(x => x.Value).Returns("72");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:EscalationCooldownHours"))
            .Returns(escalationCooldown.Object);
    }
}