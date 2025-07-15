using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using MigrationService.Tests.OneDrive.TestUtilities;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveDetectorSyncTests
{
    private readonly Mock<ILogger<OneDriveDetector>> _loggerMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IOneDriveProcessDetector> _processDetectorMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly MockOneDriveAttributeService _attributeService;
    private readonly OneDriveDetector _detector;

    public OneDriveDetectorSyncTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveDetector>>();
        _registryMock = new Mock<IOneDriveRegistry>();
        _processDetectorMock = new Mock<IOneDriveProcessDetector>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _attributeService = new MockOneDriveAttributeService();

        _detector = new OneDriveDetector(
            _loggerMock.Object,
            _registryMock.Object,
            _processDetectorMock.Object,
            _fileSystemServiceMock.Object,
            _attributeService);

        SetupOneDriveFolderMocks();
        SetupAttributeServiceMocks();
    }

    private void SetupOneDriveFolderMocks()
    {
        var syncFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder
            {
                LocalPath = @"C:\Users\TestUser\OneDrive - Contoso",
                FolderType = SyncFolderType.Business,
                DisplayName = "OneDrive - Contoso",
                IsSyncing = true,
                HasErrors = false
            },
            new OneDriveSyncFolder
            {
                LocalPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents",
                FolderType = SyncFolderType.Business,
                DisplayName = "Documents - Contoso",
                IsSyncing = true,
                HasErrors = false
            }
        };

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(string.Empty, null))
            .ReturnsAsync(syncFolders);
    }

    private void SetupAttributeServiceMocks()
    {
        // Set up common OneDrive attribute mappings
        _attributeService.SetupCommonMappings();

        // Override specific mappings for test scenarios
        // Normal file (LocalOnly when not in OneDrive folder, InSync when in OneDrive folder)
        _attributeService.SetSyncState(FileAttributes.Normal, FileSyncState.InSync);
        _attributeService.SetPinnedState(FileAttributes.Normal, false);

        // Cloud-only file (RecallOnDataAccess)
        var cloudOnlyAttributes = FileAttributes.Normal | (FileAttributes)0x00400000;
        _attributeService.SetSyncState(cloudOnlyAttributes, FileSyncState.CloudOnly);
        _attributeService.SetPinnedState(cloudOnlyAttributes, false);

        // Pinned file (LocallyAvailable)
        var pinnedAttributes = FileAttributes.Normal | (FileAttributes)0x00080000;
        _attributeService.SetSyncState(pinnedAttributes, FileSyncState.LocallyAvailable);
        _attributeService.SetPinnedState(pinnedAttributes, true);
    }

    [Fact]
    public async Task GetFileSyncStatusAsync_LocalOnlyFile_ReturnsCorrectStatus()
    {
        // Arrange
        var filePath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\localfile.txt";
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.Setup(f => f.Exists).Returns(true);
        fileInfo.Setup(f => f.Length).Returns(1024);
        fileInfo.Setup(f => f.Attributes).Returns(FileAttributes.Normal); // No cloud attributes

        _fileSystemServiceMock.Setup(f => f.GetFileInfoAsync(filePath))
            .ReturnsAsync(fileInfo.Object);

        // Act
        var result = await _detector.GetFileSyncStatusAsync(filePath);

        // Assert
        Assert.Equal(FileSyncState.LocalOnly, result.State);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(1024, result.FileSize);
        Assert.False(result.IsPinned);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public async Task GetFileSyncStatusAsync_CloudOnlyFile_ReturnsCorrectStatus()
    {
        // Arrange
        var filePath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\cloudfile.txt";
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.Setup(f => f.Exists).Returns(true);
        fileInfo.Setup(f => f.Length).Returns(0); // Cloud-only files have 0 size
        fileInfo.Setup(f => f.Attributes).Returns(FileAttributes.Normal | (FileAttributes)0x00400000); // RecallOnDataAccess

        _fileSystemServiceMock.Setup(f => f.GetFileInfoAsync(filePath))
            .ReturnsAsync(fileInfo.Object);

        // Act
        var result = await _detector.GetFileSyncStatusAsync(filePath);

        // Assert
        Assert.Equal(FileSyncState.CloudOnly, result.State);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(0, result.FileSize);
        Assert.False(result.IsPinned);
    }

    [Fact]
    public async Task GetFileSyncStatusAsync_LocallyAvailableFile_ReturnsCorrectStatus()
    {
        // Arrange
        var filePath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\availablefile.txt";
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.Setup(f => f.Exists).Returns(true);
        fileInfo.Setup(f => f.Length).Returns(2048);
        fileInfo.Setup(f => f.Attributes).Returns(FileAttributes.Normal | (FileAttributes)0x00080000); // Pinned

        _fileSystemServiceMock.Setup(f => f.GetFileInfoAsync(filePath))
            .ReturnsAsync(fileInfo.Object);

        // Act
        var result = await _detector.GetFileSyncStatusAsync(filePath);

        // Assert
        Assert.Equal(FileSyncState.LocallyAvailable, result.State);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(2048, result.FileSize);
        Assert.True(result.IsPinned);
    }

    [Fact]
    public async Task GetFileSyncStatusAsync_NonExistentFile_ReturnsUnknownStatus()
    {
        // Arrange
        var filePath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\notfound.txt";
        _fileSystemServiceMock.Setup(f => f.GetFileInfoAsync(filePath))
            .ReturnsAsync((IFileInfo?)null);

        // Act
        var result = await _detector.GetFileSyncStatusAsync(filePath);

        // Assert
        Assert.Equal(FileSyncState.Unknown, result.State);
        Assert.Equal(filePath, result.FilePath);
        Assert.Equal(0, result.FileSize);
        Assert.False(result.IsPinned);
    }

    [Fact]
    public async Task GetLocalOnlyFilesAsync_ReturnsOnlyLocalFiles()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var files = new[]
        {
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\local1.txt", 1024, FileAttributes.Normal).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\cloud.txt", 0, FileAttributes.Normal | (FileAttributes)0x00400000).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\local2.txt", 2048, FileAttributes.Normal).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\synced.txt", 512, FileAttributes.Normal | (FileAttributes)0x00080000).Object
        };

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(f => f.GetFilesAsync(folderPath, "*", SearchOption.AllDirectories))
            .ReturnsAsync(files);

        // Act
        var result = await _detector.GetLocalOnlyFilesAsync(folderPath);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, f => f.FilePath.EndsWith("local1.txt") && f.State == FileSyncState.LocalOnly);
        Assert.Contains(result, f => f.FilePath.EndsWith("local2.txt") && f.State == FileSyncState.LocalOnly);
    }

    [Fact]
    public async Task GetSyncProgressAsync_WithLocalOnlyFiles_TracksUploadProgress()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var files = new[]
        {
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\uploaded.txt", 1024, FileAttributes.Normal | (FileAttributes)0x00080000).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\uploading.txt", 2048, FileAttributes.Normal).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\pending.txt", 4096, FileAttributes.Normal).Object
        };

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(f => f.GetFilesAsync(folderPath, "*", SearchOption.AllDirectories))
            .ReturnsAsync(files);

        var mockAccountInfo = new OneDriveAccountInfo
        {
            UserFolder = folderPath,
            Email = "test@contoso.com"
        };

        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo> { mockAccountInfo });

        // Note: File modification time is handled by the mock creation

        // Act
        var result = await _detector.GetSyncProgressAsync(folderPath);

        // Assert
        Assert.Equal(OneDriveSyncStatus.Syncing, result.Status);
        Assert.Equal(3, result.TotalFiles);
        Assert.Equal(1, result.FilesSynced); // Only uploaded.txt is synced
        Assert.Equal(7168, result.TotalBytes); // Total size of all files
        Assert.Equal(1024, result.BytesSynced); // Only uploaded.txt is synced
        Assert.Contains(result.ActiveFiles, f => f.EndsWith("uploading.txt"));
        Assert.True(result.PercentComplete > 0);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public async Task GetSyncProgressAsync_AllFilesSynced_ReturnsComplete()
    {
        // Arrange
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents";
        var files = new[]
        {
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\file1.txt", 1024, FileAttributes.Normal | (FileAttributes)0x00080000).Object,
            CreateMockFileInfo(@"C:\Users\TestUser\OneDrive - Contoso\Documents\file2.txt", 2048, FileAttributes.Normal | (FileAttributes)0x00080000).Object
        };

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(f => f.GetFilesAsync(folderPath, "*", SearchOption.AllDirectories))
            .ReturnsAsync(files);

        var mockAccountInfo = new OneDriveAccountInfo
        {
            UserFolder = folderPath,
            Email = "test@contoso.com"
        };

        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo> { mockAccountInfo });

        // Act
        var result = await _detector.GetSyncProgressAsync(folderPath);

        // Assert
        Assert.Equal(OneDriveSyncStatus.UpToDate, result.Status);
        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(2, result.FilesSynced);
        Assert.Equal(3072, result.TotalBytes);
        Assert.Equal(3072, result.BytesSynced);
        Assert.Empty(result.ActiveFiles);
        Assert.Equal(100, result.PercentComplete);
        Assert.True(result.IsComplete);
    }

    private Mock<IFileInfo> CreateMockFileInfo(string path, long size, FileAttributes attributes)
    {
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.Setup(f => f.FullName).Returns(path);
        fileInfo.Setup(f => f.Name).Returns(Path.GetFileName(path));
        fileInfo.Setup(f => f.Exists).Returns(true);
        fileInfo.Setup(f => f.Length).Returns(size);
        fileInfo.Setup(f => f.Attributes).Returns(attributes);
        fileInfo.Setup(f => f.LastWriteTimeUtc).Returns(DateTime.UtcNow.AddHours(-1));
        return fileInfo;
    }
}