using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveManagerSyncTests
{
    private readonly Mock<ILogger<OneDriveManager>> _loggerMock;
    private readonly Mock<IOneDriveDetector> _detectorMock;
    private readonly Mock<IOneDriveStatusCache> _cacheMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IOneDriveProcessDetector> _processDetectorMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly OneDriveManager _manager;

    public OneDriveManagerSyncTests()
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
    public async Task ForceSyncAsync_WithLocalOnlyFiles_TriggersSync()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        
        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);

        var localOnlyFiles = new List<FileSyncStatus>
        {
            new FileSyncStatus { FilePath = Path.Combine(folderPath, "file1.txt"), State = FileSyncState.LocalOnly },
            new FileSyncStatus { FilePath = Path.Combine(folderPath, "file2.txt"), State = FileSyncState.LocalOnly }
        };

        _detectorMock.SetupSequence(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(localOnlyFiles)
            .ReturnsAsync(localOnlyFiles); // For retry

        var syncProgress = new SyncProgress
        {
            Status = OneDriveSyncStatus.Syncing,
            ActiveFiles = new List<string> { "file1.txt" }
        };

        _detectorMock.Setup(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncProgress);

        // Act
        await _manager.ForceSyncAsync(folderPath);

        // Assert
        _detectorMock.Verify(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()), Times.Once);
        _fileSystemServiceMock.Verify(f => f.WriteAllTextAsync(
            It.Is<string>(s => s.Contains(".onedrive_sync_trigger_")), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ForceSyncAsync_NoLocalFiles_DoesNotTriggerSync()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        
        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);

        _detectorMock.Setup(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileSyncStatus>());

        // Act
        await _manager.ForceSyncAsync(folderPath);

        // Assert
        _detectorMock.Verify(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()), Times.Once);
        _fileSystemServiceMock.Verify(f => f.WriteAllTextAsync(
            It.IsAny<string>(), 
            It.IsAny<string>(), 
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitForSyncAsync_CompletesSuccessfully()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var timeout = TimeSpan.FromMinutes(30);

        _stateManagerMock.Setup(s => s.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UserProfile { UserId = "S-1-5-21-123", UserName = "TestUser" } });

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveSyncFolder> { new OneDriveSyncFolder { LocalPath = folderPath } });

        var progressSequence = new[]
        {
            new SyncProgress { Status = OneDriveSyncStatus.Syncing, PercentComplete = 0, FilesSynced = 0, TotalFiles = 10 },
            new SyncProgress { Status = OneDriveSyncStatus.Syncing, PercentComplete = 50, FilesSynced = 5, TotalFiles = 10 },
            new SyncProgress { Status = OneDriveSyncStatus.UpToDate, PercentComplete = 100, FilesSynced = 10, TotalFiles = 10 }
        };

        var callCount = 0;
        _detectorMock.Setup(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => progressSequence[Math.Min(callCount++, progressSequence.Length - 1)]);

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);

        // Mock local only files for ForceSyncAsync
        _detectorMock.Setup(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileSyncStatus> { new FileSyncStatus { FilePath = "test.txt", State = FileSyncState.LocalOnly } });

        // Act
        var result = await _manager.WaitForSyncAsync(folderPath, timeout);

        // Assert
        Assert.True(result);
        _detectorMock.Verify(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()), Times.AtLeast(3));
    }

    [Fact]
    public async Task WaitForSyncAsync_TimesOut_ReturnsFalse()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var timeout = TimeSpan.FromMilliseconds(100); // Very short timeout

        _stateManagerMock.Setup(s => s.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UserProfile { UserId = "S-1-5-21-123", UserName = "TestUser" } });

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveSyncFolder> { new OneDriveSyncFolder { LocalPath = folderPath } });

        _detectorMock.Setup(d => d.GetSyncProgressAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncProgress { Status = OneDriveSyncStatus.Syncing, PercentComplete = 50 });

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);

        // Mock local only files for ForceSyncAsync
        _detectorMock.Setup(d => d.GetLocalOnlyFilesAsync(folderPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileSyncStatus> { new FileSyncStatus { FilePath = "test.txt", State = FileSyncState.LocalOnly } });

        // Act
        var result = await _manager.WaitForSyncAsync(folderPath, timeout);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryResolveSyncErrorsAsync_CategoriesErrorsCorrectly()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var status = new OneDriveStatus
        {
            IsSignedIn = true,
            SyncStatus = OneDriveSyncStatus.Error,
            SyncFolder = @"C:\Users\TestUser\OneDrive",
            AccountInfo = new OneDriveAccountInfo
            {
                Email = "test@contoso.com",
                SyncedFolders = new List<OneDriveSyncFolder>
                {
                    new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive", HasErrors = true }
                }
            }
        };

        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        _cacheMock.Setup(c => c.GetCachedStatus(userSid))
            .Returns((OneDriveStatus?)null);

        var syncOperation = new SyncOperation
        {
            Id = 1,
            UserSid = userSid,
            FolderPath = status.SyncFolder,
            Status = SyncOperationStatus.InProgress
        };

        _stateManagerMock.Setup(s => s.GetActiveSyncOperationAsync(userSid, status.SyncFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncOperation);

        var syncErrors = new List<SyncError>
        {
            new SyncError { Id = 1, FilePath = "file1.txt", ErrorMessage = "File not found", RetryAttempts = 0 },
            new SyncError { Id = 2, FilePath = "file2.txt", ErrorMessage = "File is locked", RetryAttempts = 1 },
            new SyncError { Id = 3, FilePath = "file3.txt", ErrorMessage = "Invalid path characters", RetryAttempts = 2 }
        };

        _stateManagerMock.Setup(s => s.GetUnresolvedSyncOperationErrorsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncErrors);

        _fileSystemServiceMock.Setup(f => f.FileExistsAsync("file1.txt"))
            .ReturnsAsync(false);

        // Act
        var result = await _manager.TryResolveSyncErrorsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(s => s.MarkSyncOperationErrorResolvedAsync(1, It.IsAny<CancellationToken>()), Times.Once);
        _stateManagerMock.Verify(s => s.RecordSyncErrorAsync(It.IsAny<SyncError>(), It.IsAny<CancellationToken>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task TryResolveSyncErrorsAsync_EscalatesAfterThreeRetries()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var status = new OneDriveStatus
        {
            IsSignedIn = true,
            SyncStatus = OneDriveSyncStatus.Error,
            SyncFolder = @"C:\Users\TestUser\OneDrive",
            AccountInfo = new OneDriveAccountInfo
            {
                Email = "test@contoso.com",
                SyncedFolders = new List<OneDriveSyncFolder>
                {
                    new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive", HasErrors = true }
                }
            }
        };

        _detectorMock.Setup(d => d.DetectOneDriveStatusAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        _cacheMock.Setup(c => c.GetCachedStatus(userSid))
            .Returns((OneDriveStatus?)null);

        var syncOperation = new SyncOperation
        {
            Id = 1,
            UserSid = userSid,
            FolderPath = status.SyncFolder,
            Status = SyncOperationStatus.InProgress
        };

        _stateManagerMock.Setup(s => s.GetActiveSyncOperationAsync(userSid, status.SyncFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncOperation);

        var syncErrors = new List<SyncError>
        {
            new SyncError 
            { 
                Id = 1, 
                FilePath = "file1.txt", 
                ErrorMessage = "Persistent error", 
                RetryAttempts = 3,
                EscalatedToIT = false
            }
        };

        _stateManagerMock.Setup(s => s.GetUnresolvedSyncOperationErrorsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncErrors);

        // Act
        await _manager.TryResolveSyncErrorsAsync(userSid);

        // Assert
        _stateManagerMock.Verify(s => s.CreateEscalationAsync(
            It.Is<ITEscalation>(e => e.UserId == userSid && e.Reason.Contains("sync errors")), 
            It.IsAny<CancellationToken>()), Times.Once);
        
        _stateManagerMock.Verify(s => s.RecordSyncErrorAsync(
            It.Is<SyncError>(e => e.EscalatedToIT == true), 
            It.IsAny<CancellationToken>()), Times.Once);
    }
}