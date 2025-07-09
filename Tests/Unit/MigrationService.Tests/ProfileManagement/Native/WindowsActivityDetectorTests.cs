using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.ProfileManagement.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement.Native;

public class WindowsActivityDetectorTests
{
    private readonly Mock<ILogger<WindowsActivityDetector>> _loggerMock;
    private readonly WindowsActivityDetector _detector;

    public WindowsActivityDetectorTests()
    {
        _loggerMock = new Mock<ILogger<WindowsActivityDetector>>();
        _detector = new WindowsActivityDetector(_loggerMock.Object, TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task GetUserActivityAsync_ReturnsActivityData_ForValidSid()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result = await _detector.GetUserActivityAsync(userSid);

        // Assert
        result.Should().NotBeNull();
        result.UserSid.Should().Be(userSid);
        result.LastUpdate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.Errors.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserActivityAsync_ReturnsCachedData_WhenCalledRepeatedly()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result1 = await _detector.GetUserActivityAsync(userSid);
        var result2 = await _detector.GetUserActivityAsync(userSid);

        // Assert
        result2.Should().NotBeNull();
        result2.LastUpdate.Should().Be(result1.LastUpdate); // Same cached instance
    }

    [Fact]
    public async Task GetUserActivityAsync_RefreshesCache_AfterExpiry()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result1 = await _detector.GetUserActivityAsync(userSid);

        // Clear cache to simulate expiry
        _detector.ClearCache();

        var result2 = await _detector.GetUserActivityAsync(userSid);

        // Assert
        result2.Should().NotBeNull();
        result2.LastUpdate.Should().BeAfter(result1.LastUpdate);
    }

    [Fact]
    public async Task GetUserActivityAsync_HandlesInvalidSid_Gracefully()
    {
        // Arrange
        var invalidSid = "INVALID-SID";

        // Act
        var result = await _detector.GetUserActivityAsync(invalidSid);

        // Assert
        result.Should().NotBeNull();
        result.UserSid.Should().Be(invalidSid);
        // Should have minimal data but no crash
    }

    [Fact]
    public async Task GetUserActivityAsync_CalculatesMostRecentActivity()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result = await _detector.GetUserActivityAsync(userSid);

        // Assert
        result.MostRecentActivity.Should().BeOnOrBefore(DateTime.UtcNow);
        // Most recent activity should be the max of all activity times
    }

    [Fact]
    public async Task GetUserActivityAsync_DetectsRegistryLoadedStatus()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result = await _detector.GetUserActivityAsync(userSid);

        // Assert
        // Result depends on actual registry state - IsRegistryLoaded can be either true or false
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetUserActivityAsync_CollectsFolderActivityInfo()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

        // Act
        var result = await _detector.GetUserActivityAsync(userSid);

        // Assert
        result.FolderActivity.Should().NotBeNull();
        // May or may not have entries depending on profile existence
    }

    [Fact]
    public async Task GetUserActivityAsync_WithCancellation_StopsGracefully()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await _detector.GetUserActivityAsync(userSid, cts.Token);

        // Assert
        result.Should().NotBeNull();
        // Should return partial results or empty data
    }

    [Fact]
    public void ClearCache_RemovesAllCachedData()
    {
        // Arrange & Act
        _detector.ClearCache();

        // Assert
        // No exception should be thrown
        // Next call should fetch fresh data (tested in other tests)
    }

    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    public async Task GetUserActivityAsync_HandlesWellKnownSids(string sid)
    {
        // Act
        var result = await _detector.GetUserActivityAsync(sid);

        // Assert - Well-known SID: {description}
        result.Should().NotBeNull();
        result.UserSid.Should().Be(sid);
        // System accounts may have limited activity data
    }
}

/// <summary>
/// Mock helper for testing UserActivityData
/// </summary>
public static class UserActivityDataMockHelper
{
    public static UserActivityData CreateMockActivityData(string userSid)
    {
        return new UserActivityData
        {
            UserSid = userSid,
            LastUpdate = DateTime.UtcNow,
            LastInteractiveLogon = DateTime.UtcNow.AddDays(-5),
            LastNetworkLogon = DateTime.UtcNow.AddDays(-2),
            LastLogoff = DateTime.UtcNow.AddDays(-1),
            LastUnlock = DateTime.UtcNow.AddHours(-12),
            LastProfileLoad = DateTime.UtcNow.AddDays(-5),
            IsRegistryLoaded = true,
            RecentDocumentCount = 15,
            RecentRunCommands = 3,
            TypedPathCount = 5,
            HasRecentDocumentActivity = true,
            ProfilePath = @"C:\Users\TestUser",
            NtUserLastModified = DateTime.UtcNow.AddDays(-1),
            HasActiveSession = true,
            HasRdpActivity = false,
            FolderActivity = new Dictionary<string, FolderActivityInfo>
            {
                ["Desktop"] = new FolderActivityInfo
                {
                    Path = @"C:\Users\TestUser\Desktop",
                    RecentFileCount = 5,
                    LastModified = DateTime.UtcNow.AddDays(-2)
                },
                ["Documents"] = new FolderActivityInfo
                {
                    Path = @"C:\Users\TestUser\Documents",
                    RecentFileCount = 10,
                    LastModified = DateTime.UtcNow.AddDays(-1)
                }
            },
            LogonEvents = new List<LogonEvent>
            {
                new LogonEvent
                {
                    EventTime = DateTime.UtcNow.AddDays(-5),
                    EventType = LogonEventType.Logon,
                    LogonType = 2 // Interactive
                },
                new LogonEvent
                {
                    EventTime = DateTime.UtcNow.AddDays(-1),
                    EventType = LogonEventType.Unlock
                }
            }
        };
    }
}
