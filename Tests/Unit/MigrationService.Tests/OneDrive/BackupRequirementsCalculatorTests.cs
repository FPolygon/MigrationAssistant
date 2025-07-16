using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Native;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class BackupRequirementsCalculatorTests
{
    private readonly Mock<ILogger<BackupRequirementsCalculator>> _loggerMock;
    private readonly Mock<IUserProfileManager> _profileManagerMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly BackupRequirementsCalculator _calculator;

    public BackupRequirementsCalculatorTests()
    {
        _loggerMock = new Mock<ILogger<BackupRequirementsCalculator>>();
        _profileManagerMock = new Mock<IUserProfileManager>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _configurationMock = new Mock<IConfiguration>();

        // Setup default configuration
        var configSection = new Mock<IConfigurationSection>();
        configSection.Setup(x => x.Value).Returns("0.7");
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:QuotaManagement:DefaultCompressionFactor")).Returns(configSection.Object);

        _calculator = new BackupRequirementsCalculator(
            _loggerMock.Object,
            _profileManagerMock.Object,
            _fileSystemServiceMock.Object,
            _configurationMock.Object);
    }

    [Fact]
    public async Task CalculateAsync_WithValidUser_ReturnsBackupRequirements()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = new UserProfile
        {
            UserId = userSid,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileSizeBytes = 1024 * 1024 * 1024, // 1GB
            IsActive = true
        };

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        // Act
        var result = await _calculator.CalculateAsync(userSid);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(userSid, result.UserId);
        Assert.True(result.ProfileSizeMB > 0);
        Assert.True(result.EstimatedBackupSizeMB > 0);
        Assert.True(result.RequiredSpaceMB > 0);
        Assert.Equal(0.7, result.CompressionFactor, 1);
        Assert.True(result.LastCalculated > DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task CalculateAsync_WithNonExistentUser_ThrowsArgumentException()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserProfile?)null);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _calculator.CalculateAsync(userSid));
    }

    [Fact]
    public async Task CalculateAsync_WithCustomCompressionFactor_UsesCustomValue()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var customCompressionFactor = 0.5;
        var userProfile = CreateTestUserProfile(userSid);

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        // Act
        var result = await _calculator.CalculateAsync(userSid, customCompressionFactor);

        // Assert
        Assert.Equal(customCompressionFactor, result.CompressionFactor);
    }

    [Fact]
    public async Task CalculateAsync_WithLargeProfile_CalculatesCorrectSizes()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = new UserProfile
        {
            UserId = userSid,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileSizeBytes = 5L * 1024 * 1024 * 1024, // 5GB
            IsActive = true
        };

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        // Act
        var result = await _calculator.CalculateAsync(userSid);

        // Assert
        Assert.Equal(5120, result.ProfileSizeMB); // 5GB in MB
        Assert.True(result.EstimatedBackupSizeMB < result.ProfileSizeMB); // Should be compressed
        Assert.True(result.RequiredSpaceMB >= result.EstimatedBackupSizeMB); // Should include buffer
    }

    [Fact]
    public async Task CalculateAsync_WithFileSystemError_HandlesGracefully()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid);

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        _fileSystemServiceMock.Setup(x => x.GetDirectorySizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Access denied"));

        // Act
        var result = await _calculator.CalculateAsync(userSid);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.EstimatedBackupSizeMB > 0); // Should use profile size as fallback
    }

    [Theory]
    [InlineData(0.1, 100, 110)] // 10% compression, 10% buffer
    [InlineData(0.5, 100, 55)]  // 50% compression, 10% buffer  
    [InlineData(0.9, 100, 99)]  // 90% compression, 10% buffer
    public async Task CalculateAsync_WithDifferentCompressionFactors_CalculatesCorrectly(
        double compressionFactor, long profileSizeMB, long expectedMinRequiredMB)
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = new UserProfile
        {
            UserId = userSid,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileSizeBytes = profileSizeMB * 1024 * 1024,
            IsActive = true
        };

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        // Act
        var result = await _calculator.CalculateAsync(userSid, compressionFactor);

        // Assert
        Assert.True(result.RequiredSpaceMB >= expectedMinRequiredMB - 1); // Allow for rounding
        Assert.True(result.RequiredSpaceMB <= expectedMinRequiredMB + 10); // Allow for buffer
    }

    [Fact]
    public async Task CalculateAsync_IncludesFolderBreakdown()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid);

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        // Act
        var result = await _calculator.CalculateAsync(userSid);

        // Assert
        Assert.NotNull(result.FolderBreakdown);
        Assert.NotEmpty(result.FolderBreakdown);
        Assert.Contains("Desktop", result.FolderBreakdown.Keys);
        Assert.Contains("Documents", result.FolderBreakdown.Keys);
        Assert.True(result.FolderBreakdown.Values.All(size => size >= 0));
    }

    [Fact]
    public async Task CalculateAsync_WithTimeout_CompletesWithinTimeout()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid);
        var timeout = TimeSpan.FromSeconds(1);

        _profileManagerMock.Setup(x => x.GetProfileAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(userProfile);

        SetupFileSystemMocks();

        using var cts = new CancellationTokenSource(timeout);

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await _calculator.CalculateAsync(userSid, cancellationToken: cts.Token);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.True(stopwatch.ElapsedMilliseconds < timeout.TotalMilliseconds * 2); // Some buffer for test execution
    }

    private UserProfile CreateTestUserProfile(string userSid)
    {
        return new UserProfile
        {
            UserId = userSid,
            UserName = "testuser",
            ProfilePath = @"C:\Users\testuser",
            ProfileSizeBytes = 1024 * 1024 * 1024, // 1GB
            IsActive = true
        };
    }

    private void SetupFileSystemMocks()
    {
        _fileSystemServiceMock.Setup(x => x.GetDirectorySizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100 * 1024 * 1024); // 100MB per folder

        _fileSystemServiceMock.Setup(x => x.DirectoryExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }
}