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
    private readonly Mock<IOneDriveDetector> _detectorMock;
    private readonly Mock<IOneDriveStatusCache> _cacheMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IOneDriveProcessDetector> _processDetectorMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly OneDriveManager _manager;

    public OneDriveManagerTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveManager>>();
        _detectorMock = new Mock<IOneDriveDetector>();
        _cacheMock = new Mock<IOneDriveStatusCache>();
        _registryMock = new Mock<IOneDriveRegistry>();
        _processDetectorMock = new Mock<IOneDriveProcessDetector>();
        _stateManagerMock = new Mock<IStateManager>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();

        _manager = new OneDriveManager(
            _loggerMock.Object,
            _detectorMock.Object,
            _cacheMock.Object,
            _registryMock.Object,
            _processDetectorMock.Object,
            _stateManagerMock.Object,
            _fileSystemServiceMock.Object);
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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns(cachedStatus);

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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns((OneDriveStatus?)null);
        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns(status);

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
        _detectorMock.Setup(d => d.GetSyncProgressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncProgress);

        // Configure file system mock to recognize the subfolder exists
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(subFolder))
            .ReturnsAsync(true);

        // Act
        var result = await _manager.EnsureFolderSyncedAsync(subFolder);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ForceSyncAsync_CreatesAndDeletesTriggerFile()
    {
        // Arrange
        var testFolder = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        
        // Mock directory exists
        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(testFolder))
            .ReturnsAsync(true);

        // Mock local only files to trigger sync initially, then empty list to indicate sync completion
        var localOnlyFiles = new List<FileSyncStatus>
        {
            new FileSyncStatus { FilePath = Path.Combine(testFolder, "test.txt"), State = FileSyncState.LocalOnly }
        };

        _detectorMock.SetupSequence(d => d.GetLocalOnlyFilesAsync(testFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localOnlyFiles)  // First call - has files to sync
            .ReturnsAsync(new List<FileSyncStatus>()); // Subsequent calls - no files to sync

        // Mock sync progress to indicate syncing then complete
        _detectorMock.SetupSequence(d => d.GetSyncProgressAsync(testFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncProgress { Status = OneDriveSyncStatus.Syncing })
            .ReturnsAsync(new SyncProgress { Status = OneDriveSyncStatus.UpToDate });

        // Act
        await _manager.ForceSyncAsync(testFolder);

        // Assert - Verify that trigger file was created and deleted
        _fileSystemServiceMock.Verify(f => f.WriteAllTextAsync(
            It.Is<string>(s => s.Contains(".onedrive_sync_trigger_")), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), Times.Once);

        _fileSystemServiceMock.Verify(f => f.DeleteFileAsync(
            It.Is<string>(s => s.Contains(".onedrive_sync_trigger_")), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task WaitForSyncAsync_WhenAlreadyComplete_ReturnsTrue()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";

        var progress = new SyncProgress
        {
            FolderPath = folderPath,
            Status = MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate,
            PercentComplete = 100
        };

        _detectorMock.Setup(d => d.GetSyncProgressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(progress);

        // Act
        var result = await _manager.WaitForSyncAsync(folderPath, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(result);
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

            _detectorMock.Setup(d => d.GetSyncProgressAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns(status);

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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns((OneDriveStatus?)null);
        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
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

        _cacheMock.Setup(c => c.GetCachedStatus(It.IsAny<string>())).Returns(status);

        // Act
        var result = await _manager.TryResolveSyncErrorsAsync(userSid);

        // Assert
        Assert.True(result);
    }
}
