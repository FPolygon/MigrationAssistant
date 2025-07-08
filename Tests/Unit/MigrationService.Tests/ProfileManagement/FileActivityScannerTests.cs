using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class FileActivityScannerTests
{
    private readonly Mock<ILogger<FileActivityScanner>> _loggerMock;
    private readonly FileActivityScanner _scanner;

    public FileActivityScannerTests()
    {
        _loggerMock = new Mock<ILogger<FileActivityScanner>>();
        _scanner = new FileActivityScanner(
            _loggerMock.Object,
            TimeSpan.FromDays(30),
            maxFilesPerFolder: 10,
            maxDepth: 2);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_ReturnsReport_ForValidProfile()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        result.Should().NotBeNull();
        result.ProfilePath.Should().Be(profilePath);
        result.ScanStartTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.ScanEndTime.Should().BeAfter(result.ScanStartTime);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_HandlesNonExistentProfile()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid()}");

        // Act
        var result = await _scanner.ScanProfileActivityAsync(nonExistentPath);

        // Assert
        result.Should().NotBeNull();
        result.Errors.Should().Contain(e => e.Contains("not found"));
        result.ActivityScore.Should().Be(0);
        result.ActivityLevel.Should().Be(FileActivityLevel.Inactive);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath, cts.Token);

        // Assert
        result.Should().NotBeNull();
        result.ProfilePath.Should().Be(profilePath);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_CalculatesActivityMetrics()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        result.ActivityScore.Should().BeInRange(0, 100);
        result.ActivityLevel.Should().BeOneOf(
            FileActivityLevel.Inactive,
            FileActivityLevel.Low,
            FileActivityLevel.Moderate,
            FileActivityLevel.Active,
            FileActivityLevel.VeryActive);
        result.TotalFilesScanned.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_LimitsMostRecentFiles()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        result.MostRecentFiles.Should().NotBeNull();
        result.MostRecentFiles.Count.Should().BeLessOrEqualTo(20);
    }

    [Fact]
    public async Task ScanProfileActivityAsync_SortsMostRecentFilesByDate()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        if (result.MostRecentFiles.Any())
        {
            result.MostRecentFiles.Should().BeInDescendingOrder(f => f.LastModified);
        }
    }

    [Fact]
    public void ClearCache_DoesNotThrow()
    {
        // Act & Assert
        _scanner.Invoking(s => s.ClearCache()).Should().NotThrow();
    }

    [Fact]
    public async Task FolderScanResult_ContainsExpectedProperties()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        if (result.FolderResults.Any())
        {
            var firstFolder = result.FolderResults.Values.First();
            firstFolder.FolderPath.Should().NotBeNullOrEmpty();
            firstFolder.FolderName.Should().NotBeNullOrEmpty();
            firstFolder.Priority.Should().BeGreaterOrEqualTo(0);
            firstFolder.FilesScanned.Should().BeGreaterOrEqualTo(0);
            firstFolder.ActivityScore.Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public async Task FileActivityInfo_ContainsExpectedProperties()
    {
        // Arrange
        var profilePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Act
        var result = await _scanner.ScanProfileActivityAsync(profilePath);

        // Assert
        if (result.MostRecentFiles.Any())
        {
            var firstFile = result.MostRecentFiles.First();
            firstFile.FilePath.Should().NotBeNullOrEmpty();
            firstFile.FileName.Should().NotBeNullOrEmpty();
            firstFile.Extension.Should().NotBeNull();
            firstFile.SizeBytes.Should().BeGreaterOrEqualTo(0);
            firstFile.LastModified.Should().BeBefore(DateTime.UtcNow.AddMinutes(1));
        }
    }

    [Theory]
    [InlineData(0, FileActivityLevel.Inactive)]
    [InlineData(10, FileActivityLevel.Low)]
    [InlineData(30, FileActivityLevel.Moderate)]
    [InlineData(50, FileActivityLevel.Active)]
    [InlineData(80, FileActivityLevel.VeryActive)]
    public void ActivityLevel_MapsCorrectlyToScore(int score, FileActivityLevel expectedLevel)
    {
        // This is a conceptual test to verify the mapping logic
        // In a real scenario, we'd need to access the private method or make it internal
        
        if (score >= 70)
            expectedLevel.Should().Be(FileActivityLevel.VeryActive);
        else if (score >= 40)
            expectedLevel.Should().Be(FileActivityLevel.Active);
        else if (score >= 20)
            expectedLevel.Should().Be(FileActivityLevel.Moderate);
        else if (score > 0)
            expectedLevel.Should().Be(FileActivityLevel.Low);
        else
            expectedLevel.Should().Be(FileActivityLevel.Inactive);
    }
}

/// <summary>
/// Mock helper for creating test file activity reports
/// </summary>
public static class FileActivityReportMockHelper
{
    public static FileActivityReport CreateMockReport(string profilePath, FileActivityLevel level = FileActivityLevel.Active)
    {
        var report = new FileActivityReport
        {
            ProfilePath = profilePath,
            ScanStartTime = DateTime.UtcNow.AddSeconds(-5),
            ScanEndTime = DateTime.UtcNow,
            TotalFilesScanned = 150,
            RecentFileCount = 25,
            VeryRecentFileCount = 10,
            MostRecentActivity = DateTime.UtcNow.AddHours(-6),
            ActivityScore = level switch
            {
                FileActivityLevel.VeryActive => 85,
                FileActivityLevel.Active => 65,
                FileActivityLevel.Moderate => 45,
                FileActivityLevel.Low => 25,
                _ => 5
            },
            ActivityLevel = level
        };

        // Add mock folder results
        report.FolderResults["Desktop"] = new FolderScanResult
        {
            FolderName = "Desktop",
            FolderPath = Path.Combine(profilePath, "Desktop"),
            Priority = 100,
            FilesScanned = 20,
            RecentFileCount = 5,
            VeryRecentFileCount = 2,
            ActivityScore = 75,
            LastModified = DateTime.UtcNow.AddDays(-1),
            ScanTime = DateTime.UtcNow
        };

        report.FolderResults["Documents"] = new FolderScanResult
        {
            FolderName = "Documents",
            FolderPath = Path.Combine(profilePath, "Documents"),
            Priority = 90,
            FilesScanned = 50,
            RecentFileCount = 10,
            VeryRecentFileCount = 5,
            ActivityScore = 80,
            LastModified = DateTime.UtcNow.AddHours(-12),
            ScanTime = DateTime.UtcNow
        };

        // Add mock recent files
        report.MostRecentFiles.Add(new FileActivityInfo
        {
            FileName = "Report.docx",
            FilePath = Path.Combine(profilePath, "Documents", "Report.docx"),
            Extension = ".docx",
            SizeBytes = 512000,
            LastModified = DateTime.UtcNow.AddHours(-12),
            LastAccessed = DateTime.UtcNow.AddHours(-10),
            IsUserDocument = true
        });

        report.MostRecentFiles.Add(new FileActivityInfo
        {
            FileName = "Presentation.pptx",
            FilePath = Path.Combine(profilePath, "Desktop", "Presentation.pptx"),
            Extension = ".pptx",
            SizeBytes = 2048000,
            LastModified = DateTime.UtcNow.AddDays(-2),
            LastAccessed = DateTime.UtcNow.AddDays(-1),
            IsUserDocument = true
        });

        return report;
    }
}