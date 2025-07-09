using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class ProfileActivityAnalyzerTests : IDisposable
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
            minimumActiveSizeBytes: 10 * 1024); // Lower to 10KB for testing

        // Create a temporary test directory in the system temp path
        // NOTE: We intentionally use the temp path to test that the ProfileActivityAnalyzer
        // correctly handles profiles located in paths that contain excluded folder names.
        // The analyzer should only exclude subdirectories matching excluded patterns,
        // not profile roots that happen to be in such paths.
        var tempBase = Path.GetTempPath();
        var testDirName = "MigrationTestProfile_" + Guid.NewGuid().ToString("N")[..8];
        _testProfilePath = Path.Combine(tempBase, testDirName);
        Directory.CreateDirectory(_testProfilePath);
    }

    public void Dispose()
    {
        // Cleanup
        if (Directory.Exists(_testProfilePath))
        {
            try
            {
                Directory.Delete(_testProfilePath, recursive: true);
            }
            catch (Exception ex)
            {
                // Log the error but don't throw to avoid masking test failures
                System.Diagnostics.Debug.WriteLine($"Failed to cleanup test directory {_testProfilePath}: {ex.Message}");
            }
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
        // IMPORTANT: This test intentionally creates a profile in the system temp directory
        // to verify that profiles located in paths containing excluded folder names
        // (like "Temp") are not incorrectly excluded from size calculations.
        // The ProfileActivityAnalyzer should only exclude actual subdirectories that match
        // excluded patterns, not profile roots that happen to be in paths containing those patterns.

        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);

        // Create some test files with specific content to ensure proper size
        await CreateTestFile(Path.Combine(_testProfilePath, "test1.txt"), 1024); // 1KB
        await CreateTestFile(Path.Combine(_testProfilePath, "test2.txt"), 2048); // 2KB

        var subDir = Path.Combine(_testProfilePath, "Documents");
        Directory.CreateDirectory(subDir);
        await CreateTestFile(Path.Combine(subDir, "doc1.txt"), 4096); // 4KB

        // Ensure files are properly written by waiting a bit
        await Task.Delay(100);

        // Verify files exist and have expected sizes
        var file1 = new FileInfo(Path.Combine(_testProfilePath, "test1.txt"));
        var file2 = new FileInfo(Path.Combine(_testProfilePath, "test2.txt"));
        var file3 = new FileInfo(Path.Combine(subDir, "doc1.txt"));

        file1.Exists.Should().BeTrue();
        file2.Exists.Should().BeTrue();
        file3.Exists.Should().BeTrue();

        var expectedSize = file1.Length + file2.Length + file3.Length;
        expectedSize.Should().BeGreaterThanOrEqualTo(7168);

        // Act - Retry up to 3 times with increasing delays to handle CI filesystem delays
        ProfileMetrics? result = null;
        for (int retry = 0; retry < 3; retry++)
        {
            result = await _analyzer.AnalyzeProfileAsync(profile);

            if (result.ProfileSizeBytes >= expectedSize)
            {
                break;
            }

            // If size is still 0, wait and retry
            if (retry < 2)
            {
                await Task.Delay((retry + 1) * 200); // 200ms, 400ms

                // Force file system metadata refresh
                file1.Refresh();
                file2.Refresh();
                file3.Refresh();
            }
        }

        // Assert
        result.Should().NotBeNull();
        result!.IsAccessible.Should().BeTrue();

        // Add diagnostic information if size calculation fails
        if (result.ProfileSizeBytes < expectedSize)
        {
            var diagnostics = $"Profile size mismatch. Expected: >={expectedSize}, Actual: {result.ProfileSizeBytes}. " +
                             $"Profile path: {_testProfilePath}. " +
                             $"Files exist: f1={file1.Exists}, f2={file2.Exists}, f3={file3.Exists}. " +
                             $"Actual sizes: f1={file1.Length}, f2={file2.Length}, f3={file3.Length}. " +
                             $"Errors: {string.Join(", ", result.Errors)}";
            throw new Exception(diagnostics);
        }

        result.ProfileSizeBytes.Should().BeGreaterThanOrEqualTo(expectedSize);
    }

    [Fact]
    public async Task AnalyzeProfileAsync_DetectsRecentActivity_WhenFilesRecentlyModified()
    {
        // Arrange
        var recentTime = DateTime.UtcNow.AddHours(-1);
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);
        // Ensure profile's LastLoginTime is older than the file we're about to create
        profile.LastLoginTime = DateTime.UtcNow.AddDays(-10);

        // Create a recently modified file in Documents folder (which is monitored)
        var documentsDir = Path.Combine(_testProfilePath, "Documents");
        Directory.CreateDirectory(documentsDir);
        var recentFile = Path.Combine(documentsDir, "recent.txt");
        await CreateTestFile(recentFile, 1024);

        // Set file modification time to 1 hour ago and verify it was set correctly
        File.SetLastWriteTimeUtc(recentFile, recentTime);

        // Also set the directory time to ensure it's considered
        Directory.SetLastWriteTimeUtc(documentsDir, recentTime);

        // Verify the file time was set correctly to avoid timezone issues
        var fileInfo = new FileInfo(recentFile);
        fileInfo.LastWriteTimeUtc.Should().BeCloseTo(recentTime, TimeSpan.FromSeconds(5));

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.HasRecentActivity.Should().BeTrue();
        // ProfileActivityAnalyzer returns UTC times, so we should verify UTC times
        result.LastActivityTime.Should().BeCloseTo(recentTime, TimeSpan.FromMinutes(5));
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
            ProfileSizeBytes = 5 * 1024, // 5KB (below 10KB threshold set in constructor)
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
        await CreateTestFile(regularFile, 1024); // 1KB

        // Create files in excluded folders
        // NOTE: This test verifies that ProfileActivityAnalyzer correctly excludes only subdirectories
        // that match the exclusion patterns relative to the profile root. Even though the test profile
        // may be located in a temp directory, the analyzer should not exclude files based on the
        // profile's absolute location - only based on relative paths within the profile.
        var tempDir = Path.Combine(_testProfilePath, @"AppData\Local\Temp");
        Directory.CreateDirectory(tempDir);
        await CreateTestFile(Path.Combine(tempDir, "temp.txt"), 10 * 1024 * 1024); // 10MB - should be excluded

        // Get baseline size before running the analyzer
        var baselineSize = GetDirectorySize(_testProfilePath, excludeTemp: true);

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.IsAccessible.Should().BeTrue();

        // The result should be close to our baseline calculation (allowing some variance)
        var tolerance = (long)(baselineSize * 0.1); // Allow 10% variance
        result.ProfileSizeBytes.Should().BeInRange(baselineSize - tolerance, baselineSize + tolerance);

        // And definitely should not include the 10MB temp file
        result.ProfileSizeBytes.Should().BeLessThan(5 * 1024 * 1024); // Should be much less than 10MB
    }

    private long GetDirectorySize(string path, bool excludeTemp)
    {
        // Match the exact exclusion logic from ProfileActivityAnalyzer
        var excludedFolders = new[]
        {
            @"AppData\Local\Microsoft\Windows\INetCache",
            @"AppData\Local\Microsoft\Windows\WebCache",
            @"AppData\Local\Temp",
            @"AppData\Local\Microsoft\Windows\Temporary Internet Files",
            @"AppData\Local\Packages",
            @"AppData\Local\Microsoft\WindowsApps"
        };

        long size = 0;
        var dir = new DirectoryInfo(path);

        foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
        {
            if (excludeTemp)
            {
                // Calculate relative path from profile root, matching ProfileActivityAnalyzer logic
                var relativePath = Path.GetRelativePath(path, file.FullName);

                // Check if the relative path starts with any excluded folder
                var shouldExclude = excludedFolders.Any(excludedFolder =>
                {
                    var normalizedExcluded = excludedFolder.Replace('/', Path.DirectorySeparatorChar);
                    return relativePath.StartsWith(normalizedExcluded, StringComparison.OrdinalIgnoreCase);
                });

                if (shouldExclude)
                {
                    continue;
                }
            }

            size += file.Length;
        }

        return size;
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

    [Fact]
    public async Task AnalyzeProfileAsync_ProfileInTempPath_ShouldCalculateSizeCorrectly()
    {
        // This test specifically validates the fix for the bug where profiles in paths
        // containing excluded folder names (like Temp) were incorrectly excluded entirely

        // Arrange - Profile is in temp path but should still calculate size
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);

        // Create test files
        await CreateTestFile(Path.Combine(_testProfilePath, "file1.txt"), 1024); // 1KB
        await CreateTestFile(Path.Combine(_testProfilePath, "file2.txt"), 2048); // 2KB

        // Create a file in an actually excluded subdirectory
        var tempSubDir = Path.Combine(_testProfilePath, @"AppData\Local\Temp");
        Directory.CreateDirectory(tempSubDir);
        await CreateTestFile(Path.Combine(tempSubDir, "temp.txt"), 4096); // 4KB - should be excluded

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.IsAccessible.Should().BeTrue();
        // Should include file1 and file2 (3KB) but NOT temp.txt
        result.ProfileSizeBytes.Should().BeGreaterThanOrEqualTo(3072);
        result.ProfileSizeBytes.Should().BeLessThan(7168); // Should not include the 4KB temp file
    }

    [Fact]
    public async Task AnalyzeProfileAsync_ExcludedSubdirectories_ShouldBeExcludedCorrectly()
    {
        // Test that actual subdirectories matching excluded patterns are properly excluded

        // Arrange
        var profile = CreateTestProfile("S-1-5-21-1234", "testuser", _testProfilePath);

        // Create files in profile root
        await CreateTestFile(Path.Combine(_testProfilePath, "root1.txt"), 1024); // 1KB

        // Create files in various excluded subdirectories
        var excludedPaths = new[]
        {
            @"AppData\Local\Temp",
            @"AppData\Local\Microsoft\Windows\INetCache",
            @"AppData\Local\Microsoft\Windows\WebCache",
            @"AppData\Local\Packages"
        };

        foreach (var excludedPath in excludedPaths)
        {
            var dir = Path.Combine(_testProfilePath, excludedPath);
            Directory.CreateDirectory(dir);
            await CreateTestFile(Path.Combine(dir, "excluded.dat"), 10240); // 10KB each
        }

        // Create a file in a non-excluded subdirectory
        var includedDir = Path.Combine(_testProfilePath, @"AppData\Roaming\Microsoft");
        Directory.CreateDirectory(includedDir);
        await CreateTestFile(Path.Combine(includedDir, "included.txt"), 2048); // 2KB

        // Act
        var result = await _analyzer.AnalyzeProfileAsync(profile);

        // Assert
        result.Should().NotBeNull();
        result.IsAccessible.Should().BeTrue();
        // Should only include root1.txt (1KB) and included.txt (2KB) = 3KB total
        result.ProfileSizeBytes.Should().BeGreaterThanOrEqualTo(3072);
        result.ProfileSizeBytes.Should().BeLessThan(5120); // Should not include any of the 10KB excluded files
    }

    private static async Task CreateTestFile(string path, int sizeInBytes)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Create a buffer with some recognizable content to ensure proper size
        var buffer = new byte[sizeInBytes];
        for (int i = 0; i < sizeInBytes; i++)
        {
            buffer[i] = (byte)(i % 256);
        }

        // Write the file and ensure it's flushed to disk
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(buffer);
        await stream.FlushAsync();

        // Ensure the file has normal attributes (not system or hidden)
        File.SetAttributes(path, FileAttributes.Normal);
    }
}
