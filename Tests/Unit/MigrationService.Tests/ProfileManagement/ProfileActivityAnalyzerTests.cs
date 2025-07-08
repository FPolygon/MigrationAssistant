using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class ProfileActivityAnalyzerTests
{
    private readonly Mock<ILogger<ProfileActivityAnalyzer>> _loggerMock;
    private readonly ProfileActivityAnalyzer _analyzer;
    private readonly string _testProfilePath;

    public ProfileActivityAnalyzerTests()
    {
        _loggerMock = new Mock<ILogger<ProfileActivityAnalyzer>>();
        _analyzer = new ProfileActivityAnalyzer(
            _loggerMock.Object,
            activityDetector: null,
            processDetector: null,
            recentActivityThreshold: TimeSpan.FromDays(30),
            minimumActiveSizeBytes: 100 * 1024 * 1024); // 100MB
        
        // Create a temporary test directory
        _testProfilePath = Path.Combine(Path.GetTempPath(), "TestProfile_" + Guid.NewGuid());
        Directory.CreateDirectory(_testProfilePath);
    }

    public void Dispose()
    {
        // Cleanup
        if (Directory.Exists(_testProfilePath))
        {
            Directory.Delete(_testProfilePath, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzeProfileAsync_ReturnsCorrupted_WhenProfileDirectoryDoesNotExist()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", @"C:\NonExistent\Path");

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.IsAccessible.Should().BeFalse();
        result.Classification.Should().Be(ProfileClassification.Corrupted);
        result.Errors.Should().Contain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task AnalyzeProfileAsync_CalculatesProfileSize_WhenAccessible()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        
        // Create some test files
        await CreateTestFile(Path.Combine(_testProfilePath, "test1.txt"), 1024); // 1KB
        await CreateTestFile(Path.Combine(_testProfilePath, "test2.txt"), 2048); // 2KB
        
        var subDir = Path.Combine(_testProfilePath, "Documents");
        Directory.CreateDirectory(subDir);
        await CreateTestFile(Path.Combine(subDir, "doc1.txt"), 4096); // 4KB

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.IsAccessible.Should().BeTrue();
        result.ProfileSizeBytes.Should().BeGreaterThanOrEqualTo(7168); // At least 7KB
    }

    [Fact]
    public async Task AnalyzeProfileAsync_DetectsRecentActivity_WhenFilesRecentlyModified()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        
        // Create a recently modified file
        var recentFile = Path.Combine(_testProfilePath, "recent.txt");
        await CreateTestFile(recentFile, 1024);
        File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.HasRecentActivity.Should().BeTrue();
        result.LastActivityTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromHours(2));
    }

    [Fact]
    public async Task AnalyzeProfileAsync_DetectsNoRecentActivity_WhenFilesOld()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        
        // Create an old file
        var oldFile = Path.Combine(_testProfilePath, "old.txt");
        await CreateTestFile(oldFile, 1024);
        var oldDate = DateTime.UtcNow.AddDays(-60);
        File.SetLastWriteTimeUtc(oldFile, oldDate);
        Directory.SetLastWriteTimeUtc(_testProfilePath, oldDate);

        // Update profile last login to be old too
        profile.LastLoginTime = oldDate;

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.HasRecentActivity.Should().BeFalse();
        result.LastActivityTime.Should().BeCloseTo(oldDate, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void IsProfileActive_ReturnsTrue_WhenProfileHasRecentActivity()
    {
        // Arrange
        var metrics = new ProfileMetrics
        {
            IsAccessible = true,
            ProfileSizeBytes = 200 * 1024 * 1024, // 200MB
            HasRecentActivity = true,
            IsLoaded = false,
            ActiveProcessCount = 0
        };

        // Act
        var result = _analyzer.IsProfileActive(metrics);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsProfileActive_ReturnsTrue_WhenProfileIsLoaded()
    {
        // Arrange
        var metrics = new ProfileMetrics
        {
            IsAccessible = true,
            ProfileSizeBytes = 200 * 1024 * 1024, // 200MB
            HasRecentActivity = false,
            IsLoaded = true,
            ActiveProcessCount = 0
        };

        // Act
        var result = _analyzer.IsProfileActive(metrics);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsProfileActive_ReturnsTrue_WhenProfileHasActiveProcesses()
    {
        // Arrange
        var metrics = new ProfileMetrics
        {
            IsAccessible = true,
            ProfileSizeBytes = 200 * 1024 * 1024, // 200MB
            HasRecentActivity = false,
            IsLoaded = false,
            ActiveProcessCount = 5
        };

        // Act
        var result = _analyzer.IsProfileActive(metrics);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsProfileActive_ReturnsFalse_WhenProfileTooSmall()
    {
        // Arrange
        var metrics = new ProfileMetrics
        {
            IsAccessible = true,
            ProfileSizeBytes = 50 * 1024 * 1024, // 50MB (below 100MB threshold)
            HasRecentActivity = true,
            IsLoaded = true,
            ActiveProcessCount = 1
        };

        // Act
        var result = _analyzer.IsProfileActive(metrics);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsProfileActive_ReturnsFalse_WhenProfileNotAccessible()
    {
        // Arrange
        var metrics = new ProfileMetrics
        {
            IsAccessible = false,
            ProfileSizeBytes = 200 * 1024 * 1024,
            HasRecentActivity = true,
            IsLoaded = true,
            ActiveProcessCount = 1
        };

        // Act
        var result = _analyzer.IsProfileActive(metrics);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task AnalyzeProfileAsync_ExcludesSpecificFolders_FromSizeCalculation()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        
        // Create a file in a regular folder
        var regularFile = Path.Combine(_testProfilePath, "regular.txt");
        await CreateTestFile(regularFile, 1024);

        // Create files in excluded folders
        var tempDir = Path.Combine(_testProfilePath, @"AppData\Local\Temp");
        Directory.CreateDirectory(tempDir);
        await CreateTestFile(Path.Combine(tempDir, "temp.txt"), 10 * 1024 * 1024); // 10MB - should be excluded

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.ProfileSizeBytes.Should().BeLessThan(5 * 1024 * 1024); // Should be much less than 10MB
    }

    [Fact]
    public async Task AnalyzeProfileAsync_ChecksCommonActivityFolders()
    {
        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        
        // Create recent activity in Documents folder
        var docsDir = Path.Combine(_testProfilePath, "Documents");
        Directory.CreateDirectory(docsDir);
        var recentDoc = Path.Combine(docsDir, "recent.docx");
        await CreateTestFile(recentDoc, 1024);
        File.SetLastWriteTimeUtc(recentDoc, DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.HasRecentActivity.Should().BeTrue();
        result.LastActivityTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromHours(2));
    }

    private static UserProfile CreateTestProfile(string sid, string userName, string profilePath)
    {
        return new UserProfile
        {
            UserId = sid,
            UserName = userName,
            ProfilePath = profilePath,
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-5),
            IsActive = false,
            ProfileSizeBytes = 0,
            RequiresBackup = true,
            BackupPriority = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static async Task CreateTestFile(string path, int sizeInBytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var buffer = new byte[sizeInBytes];
        await File.WriteAllBytesAsync(path, buffer);
    }
}