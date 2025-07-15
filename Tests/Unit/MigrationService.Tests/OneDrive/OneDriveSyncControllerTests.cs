using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Moq;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Xunit;

namespace MigrationService.Tests.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveSyncControllerTests
{
    private readonly Mock<ILogger<OneDriveSyncController>> _loggerMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IOneDriveDetector> _detectorMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly OneDriveSyncController _controller;

    public OneDriveSyncControllerTests()
    {
        _loggerMock = new Mock<ILogger<OneDriveSyncController>>();
        _registryMock = new Mock<IOneDriveRegistry>();
        _detectorMock = new Mock<IOneDriveDetector>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();

        _controller = new OneDriveSyncController(
            _loggerMock.Object,
            _registryMock.Object,
            _detectorMock.Object,
            _fileSystemServiceMock.Object);
    }

    [Fact]
    public async Task AddFolderToSyncScopeAsync_ValidFolder_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var syncFolder = @"C:\Users\TestUser\OneDrive - Contoso";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\Projects";

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(true);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "UserFolder"))
            .ReturnsAsync(syncFolder);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Documents\OldProjects", @"Pictures\Personal" });

        // Act
        var result = await _controller.AddFolderToSyncScopeAsync(userSid, accountId, folderPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task AddFolderToSyncScopeAsync_FolderDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\NonExistent";

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folderPath))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.AddFolderToSyncScopeAsync(userSid, accountId, folderPath);

        // Assert
        Assert.False(result);
        _registryMock.Verify(r => r.GetUserRegistryValueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RemoveFolderFromSyncScopeAsync_ValidFolder_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var syncFolder = @"C:\Users\TestUser\OneDrive - Contoso";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\Projects";

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "UserFolder"))
            .ReturnsAsync(syncFolder);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Pictures\Personal" });

        // Act
        var result = await _controller.RemoveFolderFromSyncScopeAsync(userSid, accountId, folderPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsFolderInSyncScopeAsync_FolderNotExcluded_ReturnsTrue()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\Projects";

        var syncedFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive - Contoso" }
        };

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(userSid, null))
            .ReturnsAsync(syncedFolders);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            It.IsAny<string>(),
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Pictures\Personal", @"Videos" });

        // Act
        var result = await _controller.IsFolderInSyncScopeAsync(userSid, folderPath);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task IsFolderInSyncScopeAsync_FolderExcluded_ReturnsFalse()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\Projects";

        var syncedFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive - Contoso" }
        };

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(userSid, null))
            .ReturnsAsync(syncedFolders);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            It.IsAny<string>(),
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Documents\Projects", @"Pictures\Personal" });

        // Act
        var result = await _controller.IsFolderInSyncScopeAsync(userSid, folderPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsFolderInSyncScopeAsync_ParentFolderExcluded_ReturnsFalse()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var folderPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents\Projects\SubProject";

        var syncedFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive - Contoso" }
        };

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(userSid, null))
            .ReturnsAsync(syncedFolders);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            It.IsAny<string>(),
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Documents", @"Pictures\Personal" });

        // Act
        var result = await _controller.IsFolderInSyncScopeAsync(userSid, folderPath);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetExcludedFoldersAsync_ReturnsCorrectList()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var expectedExcluded = new[] { @"Documents\Private", @"Pictures\Personal", @"Videos" };

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "ExcludedFolders"))
            .ReturnsAsync(expectedExcluded);

        // Act
        var result = await _controller.GetExcludedFoldersAsync(userSid, accountId);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Contains(@"Documents\Private", result);
        Assert.Contains(@"Pictures\Personal", result);
        Assert.Contains(@"Videos", result);
    }

    [Fact]
    public async Task GetExcludedFoldersAsync_NoExclusions_ReturnsEmptyList()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "ExcludedFolders"))
            .ReturnsAsync((object?)null);

        // Act
        var result = await _controller.GetExcludedFoldersAsync(userSid, accountId);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task EnsureCriticalFoldersIncludedAsync_ProcessesAllFolders()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var criticalFolders = new List<string>
        {
            @"C:\Users\TestUser\OneDrive - Contoso\Documents\Critical1",
            @"C:\Users\TestUser\OneDrive - Contoso\Documents\Critical2",
            @"C:\Users\TestUser\Documents\NotInOneDrive"
        };

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        var syncedFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder { LocalPath = @"C:\Users\TestUser\OneDrive - Contoso" }
        };

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(userSid, null))
            .ReturnsAsync(syncedFolders);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            It.IsAny<string>(),
            "UserFolder"))
            .ReturnsAsync(@"C:\Users\TestUser\OneDrive - Contoso");

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            It.IsAny<string>(),
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { });

        // Act
        var result = await _controller.EnsureCriticalFoldersIncludedAsync(userSid, accountId, criticalFolders);

        // Assert
        Assert.Equal(3, result.Count);
        Assert.True(result[criticalFolders[0]]);  // In OneDrive, should succeed
        Assert.True(result[criticalFolders[1]]);  // In OneDrive, should succeed
        Assert.False(result[criticalFolders[2]]); // Not in OneDrive, should fail
    }

    [Fact]
    public async Task ResetSelectiveSyncAsync_ClearsExclusions()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";

        // Act
        var result = await _controller.ResetSelectiveSyncAsync(userSid, accountId);

        // Assert
        Assert.True(result);
    }
}