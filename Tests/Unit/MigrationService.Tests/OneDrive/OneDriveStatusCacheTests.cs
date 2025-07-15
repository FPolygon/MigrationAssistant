using Microsoft.Extensions.Logging;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

public class OneDriveStatusCacheTests
{
    private readonly Mock<ILogger<OneDriveStatusCache>> _loggerMock;
    private readonly OneDriveStatusCache _cache;

    public OneDriveStatusCacheTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveStatusCache>>();
        _cache = new OneDriveStatusCache(_loggerMock.Object, TimeSpan.FromSeconds(5)); // Short expiry for tests
    }

    [Fact]
    public void GetCachedStatus_WhenNotCached_ReturnsNull()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result = _cache.GetCachedStatus(userSid);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void CacheStatus_ThenGet_ReturnsStatus()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            AccountEmail = "user@company.com",
            SyncStatus = OneDriveSyncStatus.UpToDate
        };

        // Act
        _cache.CacheStatus(userSid, status);
        var result = _cache.GetCachedStatus(userSid);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(status.IsInstalled, result.IsInstalled);
        Assert.Equal(status.AccountEmail, result.AccountEmail);
        Assert.Equal(status.SyncStatus, result.SyncStatus);
    }

    [Fact]
    public async Task GetCachedStatus_AfterExpiry_ReturnsNull()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true
        };

        // Act
        _cache.CacheStatus(userSid, status);
        await Task.Delay(TimeSpan.FromSeconds(6)); // Wait for expiry
        var result = _cache.GetCachedStatus(userSid);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void InvalidateCache_RemovesEntry()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus { IsInstalled = true };
        _cache.CacheStatus(userSid, status);

        // Act
        _cache.InvalidateCache(userSid);
        var result = _cache.GetCachedStatus(userSid);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ClearCache_RemovesAllEntries()
    {
        // Arrange
        var status1 = new OneDriveStatus { IsInstalled = true };
        var status2 = new OneDriveStatus { IsInstalled = true };
        _cache.CacheStatus("user1", status1);
        _cache.CacheStatus("user2", status2);

        // Act
        _cache.ClearCache();

        // Assert
        Assert.Null(_cache.GetCachedStatus("user1"));
        Assert.Null(_cache.GetCachedStatus("user2"));
        Assert.Equal(0, _cache.CacheSize);
    }

    [Fact]
    public void CacheSize_ReturnsCorrectCount()
    {
        // Arrange & Act
        _cache.CacheStatus("user1", new OneDriveStatus());
        _cache.CacheStatus("user2", new OneDriveStatus());
        _cache.CacheStatus("user3", new OneDriveStatus());

        // Assert
        Assert.Equal(3, _cache.CacheSize);
    }

    [Fact]
    public void GetStatistics_ReturnsCorrectStats()
    {
        // Arrange
        var oldCache = new OneDriveStatusCache(_loggerMock.Object, TimeSpan.FromMilliseconds(100));
        oldCache.CacheStatus("user1", new OneDriveStatus());
        System.Threading.Thread.Sleep(200); // Let it expire
        oldCache.CacheStatus("user2", new OneDriveStatus());

        // Act
        var stats = oldCache.GetStatistics();

        // Assert
        Assert.Equal(2, stats.TotalEntries);
        Assert.Equal(1, stats.ValidEntries);
        Assert.Equal(1, stats.ExpiredEntries);
        Assert.Equal(0.1, stats.CacheExpiryMinutes, 2); // 100ms = 0.00167 minutes
    }

    [Fact]
    public void CacheStatus_UpdatesExistingEntry()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status1 = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = false,
            AccountEmail = null
        };
        var status2 = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            AccountEmail = "user@company.com"
        };

        // Act
        _cache.CacheStatus(userSid, status1);
        _cache.CacheStatus(userSid, status2); // Update
        var result = _cache.GetCachedStatus(userSid);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSignedIn);
        Assert.Equal("user@company.com", result.AccountEmail);
        Assert.Equal(1, _cache.CacheSize); // Should still be 1 entry
    }

    [Fact]
    public async Task Cache_HandlesMultipleUsersConcurrently()
    {
        // Arrange
        var tasks = new List<Task>();
        var userCount = 100;

        // Act
        for (int i = 0; i < userCount; i++)
        {
            var userId = $"user{i}";
            var status = new OneDriveStatus { AccountEmail = $"user{i}@company.com" };

            tasks.Add(Task.Run(() => _cache.CacheStatus(userId, status)));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.Equal(userCount, _cache.CacheSize);

        // Verify a few random entries
        var result10 = _cache.GetCachedStatus("user10");
        Assert.NotNull(result10);
        Assert.Equal("user10@company.com", result10.AccountEmail);

        var result50 = _cache.GetCachedStatus("user50");
        Assert.NotNull(result50);
        Assert.Equal("user50@company.com", result50.AccountEmail);
    }
}
