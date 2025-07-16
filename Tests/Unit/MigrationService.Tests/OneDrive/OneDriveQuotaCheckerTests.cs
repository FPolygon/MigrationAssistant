using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveQuotaCheckerTests
{
    private readonly Mock<ILogger<OneDriveQuotaChecker>> _loggerMock;
    private readonly Mock<IOneDriveDetector> _oneDriveDetectorMock;
    private readonly Mock<IBackupRequirementsCalculator> _requirementsCalculatorMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly OneDriveQuotaChecker _quotaChecker;

    public OneDriveQuotaCheckerTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveQuotaChecker>>();
        _oneDriveDetectorMock = new Mock<IOneDriveDetector>();
        _requirementsCalculatorMock = new Mock<IBackupRequirementsCalculator>();
        _configurationMock = new Mock<IConfiguration>();

        SetupConfiguration();

        _quotaChecker = new OneDriveQuotaChecker(
            _loggerMock.Object,
            _oneDriveDetectorMock.Object,
            _requirementsCalculatorMock.Object,
            _configurationMock.Object);
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithSufficientSpace_ReturnsGoodHealth()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 2000); // 20% usage
        var backupRequirements = CreateBackupRequirements(requiredMB: 1000);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(QuotaHealthLevel.Good, result.HealthLevel);
        Assert.Equal(10000, result.TotalSpaceMB);
        Assert.Equal(2000, result.UsedSpaceMB);
        Assert.Equal(8000, result.AvailableSpaceMB);
        Assert.Equal(1000, result.RequiredSpaceMB);
        Assert.Equal(20.0, result.UsagePercentage);
        Assert.True(result.CanAccommodateBackup);
        Assert.Equal(0, result.ShortfallMB);
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithHighUsage_ReturnsWarningHealth()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 8500); // 85% usage
        var backupRequirements = CreateBackupRequirements(requiredMB: 1000);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Warning, result.HealthLevel);
        Assert.Equal(85.0, result.UsagePercentage);
        Assert.True(result.CanAccommodateBackup); // Still has space for backup
        Assert.Contains("high usage", result.Issues.ToLower());
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithCriticalUsage_ReturnsCriticalHealth()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 9600); // 96% usage
        var backupRequirements = CreateBackupRequirements(requiredMB: 1000);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Critical, result.HealthLevel);
        Assert.Equal(96.0, result.UsagePercentage);
        Assert.False(result.CanAccommodateBackup); // Not enough space for backup
        Assert.Equal(600, result.ShortfallMB); // 1000 required - 400 available
        Assert.Contains("critical", result.Issues.ToLower());
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithInsufficientBackupSpace_ReturnsCritical()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 7000); // 70% usage, normally good
        var backupRequirements = CreateBackupRequirements(requiredMB: 4000); // Requires 4GB but only 3GB available

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Critical, result.HealthLevel);
        Assert.False(result.CanAccommodateBackup);
        Assert.Equal(1000, result.ShortfallMB); // 4000 required - 3000 available
        Assert.Contains("insufficient space", result.Issues.ToLower());
        Assert.Contains("increase quota", result.Recommendations.ToLower());
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithOneDriveNotConfigured_ReturnsNull()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OneDriveStatus?)null);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateBackupFeasibilityAsync_WithSufficientSpace_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 2000);
        var backupRequirements = CreateBackupRequirements(requiredMB: 1000);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.ValidateBackupFeasibilityAsync(userSid);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateBackupFeasibilityAsync_WithInsufficientSpace_ReturnsFalse()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 9500);
        var backupRequirements = CreateBackupRequirements(requiredMB: 1000);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.ValidateBackupFeasibilityAsync(userSid);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(50, 1000, QuotaHealthLevel.Good)]     // 5% usage
    [InlineData(800, 1000, QuotaHealthLevel.Warning)] // 80% usage
    [InlineData(950, 1000, QuotaHealthLevel.Critical)] // 95% usage
    public async Task CheckQuotaHealthAsync_WithVariousUsageLevels_ReturnsCorrectHealthLevel(
        long usedMB, long totalMB, QuotaHealthLevel expectedHealth)
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: totalMB, usedMB: usedMB);
        var backupRequirements = CreateBackupRequirements(requiredMB: 10); // Small backup requirement

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Equal(expectedHealth, result.HealthLevel);
    }

    [Fact]
    public async Task CheckQuotaHealthAsync_WithLowFreeSpaceThreshold_DetectsIssue()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var oneDriveStatus = CreateOneDriveStatus(totalMB: 10000, usedMB: 9000); // 1GB free, but below 2GB threshold
        var backupRequirements = CreateBackupRequirements(requiredMB: 500);

        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(oneDriveStatus);

        _requirementsCalculatorMock.Setup(x => x.CalculateAsync(userSid, It.IsAny<double?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(backupRequirements);

        // Act
        var result = await _quotaChecker.CheckQuotaHealthAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Warning, result.HealthLevel);
        Assert.Contains("low free space", result.Issues.ToLower());
    }

    private OneDriveStatus CreateOneDriveStatus(long totalMB, long usedMB)
    {
        return new OneDriveStatus
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            SyncStatus = "UpToDate",
            QuotaTotalMB = totalMB,
            QuotaUsedMB = usedMB,
            QuotaAvailableMB = totalMB - usedMB,
            IsSignedIn = true,
            LastUpdated = DateTime.UtcNow
        };
    }

    private BackupRequirements CreateBackupRequirements(long requiredMB)
    {
        return new BackupRequirements
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            ProfileSizeMB = requiredMB * 2,
            EstimatedBackupSizeMB = (long)(requiredMB * 0.8),
            RequiredSpaceMB = requiredMB,
            CompressionFactor = 0.7,
            LastCalculated = DateTime.UtcNow,
            FolderBreakdown = new Dictionary<string, long>
            {
                { "Desktop", requiredMB / 4 },
                { "Documents", requiredMB / 2 },
                { "Pictures", requiredMB / 4 }
            }
        };
    }

    private void SetupConfiguration()
    {
        var warningThreshold = new Mock<IConfigurationSection>();
        warningThreshold.Setup(x => x.Value).Returns("80.0");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:WarningThresholdPercentage"))
            .Returns(warningThreshold.Object);

        var criticalThreshold = new Mock<IConfigurationSection>();
        criticalThreshold.Setup(x => x.Value).Returns("95.0");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:CriticalThresholdPercentage"))
            .Returns(criticalThreshold.Object);

        var minimumFreeSpace = new Mock<IConfigurationSection>();
        minimumFreeSpace.Setup(x => x.Value).Returns("2048");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:MinimumFreeSpaceMB"))
            .Returns(minimumFreeSpace.Object);
    }
}