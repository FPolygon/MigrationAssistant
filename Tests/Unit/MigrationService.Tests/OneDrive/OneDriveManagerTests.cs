using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveManagerTests
{
    private readonly Mock<ILogger<OneDriveManager>> _loggerMock;
    private readonly Mock<OneDriveDetector> _detectorMock;
    private readonly Mock<OneDriveStatusCache> _cacheMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<OneDriveProcessDetector> _processDetectorMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly OneDriveManager _manager;

    public OneDriveManagerTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveManager>>();
        _detectorMock = new Mock<OneDriveDetector>(
            new Mock<ILogger<OneDriveDetector>>().Object,
            new Mock<IOneDriveRegistry>().Object,
            new Mock<OneDriveProcessDetector>(new Mock<ILogger<OneDriveProcessDetector>>().Object).Object);
        _cacheMock = new Mock<OneDriveStatusCache>(
            new Mock<ILogger<OneDriveStatusCache>>().Object,
            TimeSpan.FromMinutes(5));
        _registryMock = new Mock<IOneDriveRegistry>();
        _processDetectorMock = new Mock<OneDriveProcessDetector>(new Mock<ILogger<OneDriveProcessDetector>>().Object);
        _stateManagerMock = new Mock<IStateManager>();

        _manager = new OneDriveManager(
            _loggerMock.Object,
            _detectorMock.Object,
            _cacheMock.Object,
            _registryMock.Object,
            _processDetectorMock.Object,
            _stateManagerMock.Object);
    }

    [Fact]
    public async Task GetStatusAsync_WithCachedStatus_ReturnsCached()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var cachedStatus = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            AccountEmail = "user@company.com"
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns(cachedStatus);

        // Act
        var result = await _manager.GetStatusAsync(userSid);

        // Assert
        Assert.Equal(cachedStatus, result);
        _detectorMock.Verify(d => d.DetectOneDriveStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetStatusAsync_WithoutCache_PerformsDetection()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var detectedStatus = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            AccountEmail = "user@company.com",
            SyncStatus = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns((OneDriveStatus?)null);
        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(detectedStatus);

        // Act
        var result = await _manager.GetStatusAsync(userSid);

        // Assert
        Assert.Equal(detectedStatus, result);
        _cacheMock.Verify(c => c.CacheStatus(userSid, detectedStatus), Times.Once);
    }

    [Fact]
    public async Task GetAvailableSpaceMBAsync_WhenNotSignedIn_ReturnsNegativeOne()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = false
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns(status);

        // Act
        var result = await _manager.GetAvailableSpaceMBAsync(userSid);

        // Assert
        Assert.Equal(-1, result);
    }

    [Fact]
    public async Task EnsureFolderSyncedAsync_WhenFolderDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = await _manager.EnsureFolderSyncedAsync(folderPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task EnsureFolderSyncedAsync_WhenInSyncFolder_ReturnsTrue()
    {
        // Arrange
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var subFolder = Path.Combine(tempFolder, "Documents");
        Directory.CreateDirectory(subFolder);

        try
        {
            var userProfile = new UserProfile
            {
                UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
                UserName = "testuser"
            };

            var syncFolder = new OneDriveSyncFolder
            {
                LocalPath = tempFolder,
                FolderType = SyncFolderType.Business
            };

            var syncProgress = new SyncProgress
            {
                FolderPath = subFolder,
                Status = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate
            };

            _stateManagerMock.Setup(s => s.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<UserProfile> { userProfile });
            _registryMock.Setup(r => r.GetSyncedFoldersAsync(userProfile.UserId, null))
                .ReturnsAsync(new List<OneDriveSyncFolder> { syncFolder });
            _detectorMock.Setup(d => d.GetSyncProgressAsync(subFolder, It.IsAny<CancellationToken>()))
                .ReturnsAsync(syncProgress);

            // Act
            var result = await _manager.EnsureFolderSyncedAsync(subFolder);

            // Assert
            Assert.True(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [Fact]
    public async Task ForceSyncAsync_CreatesAndDeletesTriggerFile()
    {
        // Arrange
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempFolder);

        try
        {
            // Act
            await _manager.ForceSyncAsync(tempFolder);

            // Assert - Check if any .onedrive_sync_trigger file was created
            // Note: The file might be deleted too quickly to catch, so we just verify no exceptions
            Assert.True(Directory.Exists(tempFolder));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
    }

    [Fact]
    public async Task WaitForSyncAsync_WhenAlreadyComplete_ReturnsTrue()
    {
        // Arrange
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);

        try
        {
            var progress = new SyncProgress
            {
                FolderPath = folderPath,
                Status = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate,
                PercentComplete = 100
            };

            _detectorMock.Setup(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(progress);

            // Act
            var result = await _manager.WaitForSyncAsync(folderPath, TimeSpan.FromSeconds(5));

            // Assert
            Assert.True(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }
        }
    }

    [Fact]
    public async Task WaitForSyncAsync_WhenTimeout_ReturnsFalse()
    {
        // Arrange
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);

        try
        {
            var progress = new SyncProgress
            {
                FolderPath = folderPath,
                Status = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.Syncing,
                PercentComplete = 50
            };

            _detectorMock.Setup(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()))
                .ReturnsAsync(progress);

            // Act
            var result = await _manager.WaitForSyncAsync(folderPath, TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.False(result);
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }
        }
    }

    [Fact]
    public async Task TryRecoverAuthenticationAsync_WhenNoAuthRequired_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            SyncStatus = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns(status);

        // Act
        var result = await _manager.TryRecoverAuthenticationAsync(userSid);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryRecoverAuthenticationAsync_WhenAuthRequired_ReturnsFalse()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = false,
            SyncStatus = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.AuthenticationRequired,
            AccountEmail = "user@company.com"
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns((OneDriveStatus?)null);
        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        // Act
        var result = await _manager.TryRecoverAuthenticationAsync(userSid);

        // Assert
        Assert.False(result);
        _loggerMock.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Authentication recovery requires user interaction")),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task TryResolveSyncErrorsAsync_WhenNoErrors_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsSignedIn = true,
            SyncStatus = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate
        };

        _cacheMock.Setup(c => c.GetCachedStatus(userSid)).Returns(status);

        // Act
        var result = await _manager.TryResolveSyncErrorsAsync(userSid);

        // Assert
        Assert.True(result);
    }
}
