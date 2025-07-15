using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveDetectorTests
{
    private readonly Mock<ILogger<OneDriveDetector>> _loggerMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IOneDriveProcessDetector> _processDetectorMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly OneDriveDetector _detector;

    public OneDriveDetectorTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveDetector>>();
        _registryMock = new Mock<IOneDriveRegistry>();
        _processDetectorMock = new Mock<IOneDriveProcessDetector>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _detector = new OneDriveDetector(_loggerMock.Object, _registryMock.Object, _processDetectorMock.Object, _fileSystemServiceMock.Object);
    }

    [Fact]
    public async Task DetectOneDriveStatusAsync_WhenNotInstalled_ReturnsNotInstalled()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(false);

        // Act
        var status = await _detector.DetectOneDriveStatusAsync(userSid);

        // Assert
        Assert.False(status.IsInstalled);
        Assert.Equal(OneDriveSyncStatus.Unknown, status.SyncStatus);
        _registryMock.Verify(r => r.IsOneDriveInstalled(), Times.Once);
    }

    [Fact]
    public async Task DetectOneDriveStatusAsync_WhenNoAccounts_ReturnsNotSignedIn()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo>());
        _processDetectorMock.Setup(p => p.IsOneDriveRunningForUserAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        // Act
        var status = await _detector.DetectOneDriveStatusAsync(userSid);

        // Assert
        Assert.True(status.IsInstalled);
        Assert.False(status.IsSignedIn);
        Assert.Equal(OneDriveSyncStatus.NotSignedIn, status.SyncStatus);
    }

    [Fact]
    public async Task DetectOneDriveStatusAsync_WithValidAccount_ReturnsSignedIn()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        var account = new OneDriveAccountInfo
        {
            AccountId = "Business1",
            Email = "user@company.com",
            UserFolder = tempFolder,
            IsPrimary = true
        };

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo> { account });
        _processDetectorMock.Setup(p => p.IsOneDriveRunningForUserAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _registryMock.Setup(r => r.IsSyncPausedAsync(It.IsAny<string>(), null))
            .ReturnsAsync(false);
        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveSyncFolder>());
        
        // Configure file system mock to recognize the temp folder
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(tempFolder))
            .ReturnsAsync(true);

        // Act
        var status = await _detector.DetectOneDriveStatusAsync(userSid);

        // Assert
        Assert.True(status.IsInstalled);
        Assert.True(status.IsRunning);
        Assert.True(status.IsSignedIn);
        Assert.Equal("user@company.com", status.AccountEmail);
        Assert.Equal(tempFolder, status.SyncFolder);
        Assert.NotNull(status.AccountInfo);
        Assert.Equal("Business1", status.AccountInfo.AccountId);
    }

    [Fact]
    public async Task DetectOneDriveStatusAsync_WhenSyncPaused_ReturnsPausedStatus()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        var account = new OneDriveAccountInfo
        {
            AccountId = "Business1",
            Email = "user@company.com",
            UserFolder = tempFolder,
            IsPrimary = true
        };

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo> { account });
        _processDetectorMock.Setup(p => p.IsOneDriveRunningForUserAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _registryMock.Setup(r => r.IsSyncPausedAsync(It.IsAny<string>(), null))
            .ReturnsAsync(true);
        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveSyncFolder>());
        
        // Configure file system mock to recognize the temp folder
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(tempFolder))
            .ReturnsAsync(true);

        // Act
        var status = await _detector.DetectOneDriveStatusAsync(userSid);

        // Assert
        Assert.Equal(OneDriveSyncStatus.Paused, status.SyncStatus);
    }

    [Fact]
    public async Task GetSyncProgressAsync_WhenFolderDoesNotExist_ReturnsError()
    {
        // Arrange
        var folderPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var progress = await _detector.GetSyncProgressAsync(folderPath);

        // Assert
        Assert.Equal(OneDriveSyncStatus.Error, progress.Status);
        Assert.Single(progress.Errors);
        Assert.Contains("Folder does not exist", progress.Errors[0].ErrorMessage);
    }

    [Fact]
    public async Task GetSyncProgressAsync_WithValidFolder_CalculatesProgress()
    {
        // Arrange
        var tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        // Create mock files - don't create real files, just mock them
        var mockFiles = new List<IFileInfo>();
        for (int i = 0; i < 5; i++)
        {
            var filePath = Path.Combine(tempFolder, $"file{i}.txt");
            var mockFile = new Mock<IFileInfo>();
            mockFile.Setup(f => f.FullName).Returns(filePath);
            mockFile.Setup(f => f.Length).Returns(12); // "test content".Length
            mockFile.Setup(f => f.Exists).Returns(true);
            mockFile.Setup(f => f.Attributes).Returns(FileAttributes.Normal);
            mockFiles.Add(mockFile.Object);
        }

        // Configure mocks
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(tempFolder))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(fs => fs.GetFilesAsync(tempFolder, "*", SearchOption.AllDirectories))
            .ReturnsAsync(mockFiles.ToArray());
        
        // Mock IsFileSyncedAsync to return true for all files (consider them synced)
        foreach (var file in mockFiles)
        {
            _fileSystemServiceMock.Setup(fs => fs.GetFileInfoAsync(file.FullName))
                .ReturnsAsync(file);
        }

        // Act
        var progress = await _detector.GetSyncProgressAsync(tempFolder);

        // Assert
        Assert.Equal(tempFolder, progress.FolderPath);
        Assert.Equal(5, progress.TotalFiles);
        Assert.True(progress.TotalBytes > 0);
        Assert.Equal(100, progress.PercentComplete); // All files are considered synced in test
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public async Task DetectOneDriveStatusAsync_WithMultipleAccounts_SelectsPrimary()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var tempFolder1 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var tempFolder2 = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business2",
                Email = "user2@company.com",
                UserFolder = tempFolder2,
                IsPrimary = false
            },
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user1@company.com",
                UserFolder = tempFolder1,
                IsPrimary = true
            }
        };

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(accounts);
        _processDetectorMock.Setup(p => p.IsOneDriveRunningForUserAsync(It.IsAny<string>()))
            .ReturnsAsync(true);
        _registryMock.Setup(r => r.IsSyncPausedAsync(It.IsAny<string>(), null))
            .ReturnsAsync(false);
        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveSyncFolder>());
        
        // Configure file system mock to recognize both temp folders
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(tempFolder1))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(fs => fs.DirectoryExistsAsync(tempFolder2))
            .ReturnsAsync(true);

        // Act
        var status = await _detector.DetectOneDriveStatusAsync(userSid);

        // Assert
        Assert.Equal("user1@company.com", status.AccountEmail);
        Assert.Equal(tempFolder1, status.SyncFolder);
        Assert.True(status.AccountInfo?.IsPrimary);
    }
}
