using MigrationTool.Service.OneDrive.Models;
using Xunit;

namespace MigrationService.Tests.OneDrive.Models;

public class OneDriveModelsTests
{
    [Fact]
    public void OneDriveAccountInfo_AvailableSpaceMB_CalculatesCorrectly()
    {
        // Arrange
        var account = new OneDriveAccountInfo
        {
            TotalSpaceBytes = 1024L * 1024 * 1024 * 100, // 100 GB
            UsedSpaceBytes = 1024L * 1024 * 1024 * 40   // 40 GB
        };

        // Act
        var availableMB = account.AvailableSpaceMB;

        // Assert
        Assert.Equal(60 * 1024, availableMB); // 60 GB = 61440 MB
    }

    [Fact]
    public void OneDriveAccountInfo_AvailableSpaceMB_ReturnsNegativeOneWhenNull()
    {
        // Arrange
        var account = new OneDriveAccountInfo
        {
            TotalSpaceBytes = null,
            UsedSpaceBytes = null
        };

        // Act
        var availableMB = account.AvailableSpaceMB;

        // Assert
        Assert.Equal(-1, availableMB);
    }

    [Fact]
    public void KnownFolderMoveStatus_GetRedirectedFolders_ReturnsCorrectList()
    {
        // Arrange
        var kfm = new KnownFolderMoveStatus
        {
            DesktopRedirected = true,
            DocumentsRedirected = false,
            PicturesRedirected = true
        };

        // Act
        var folders = kfm.GetRedirectedFolders();

        // Assert
        Assert.Equal(2, folders.Count);
        Assert.Contains("Desktop", folders);
        Assert.Contains("Pictures", folders);
        Assert.DoesNotContain("Documents", folders);
    }

    [Fact]
    public void KnownFolderMoveStatus_RedirectedFolderCount_CalculatesCorrectly()
    {
        // Arrange
        var kfm = new KnownFolderMoveStatus
        {
            DesktopRedirected = true,
            DocumentsRedirected = true,
            PicturesRedirected = true
        };

        // Act & Assert
        Assert.Equal(3, kfm.RedirectedFolderCount);

        // Change one
        kfm.DocumentsRedirected = false;
        Assert.Equal(2, kfm.RedirectedFolderCount);
    }

    [Fact]
    public void SyncProgress_IsComplete_WhenStatusIsUpToDate()
    {
        // Arrange
        var progress = new SyncProgress
        {
            Status = OneDriveSyncStatus.UpToDate,
            TotalFiles = 10,
            FilesSynced = 5 // Even with incomplete count
        };

        // Act & Assert
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void SyncProgress_IsComplete_WhenAllFilesSynced()
    {
        // Arrange
        var progress = new SyncProgress
        {
            Status = OneDriveSyncStatus.Syncing,
            TotalFiles = 10,
            FilesSynced = 10
        };

        // Act & Assert
        Assert.True(progress.IsComplete);
    }

    [Fact]
    public void SyncProgress_IsNotComplete_WhenSyncing()
    {
        // Arrange
        var progress = new SyncProgress
        {
            Status = OneDriveSyncStatus.Syncing,
            TotalFiles = 10,
            FilesSynced = 5
        };

        // Act & Assert
        Assert.False(progress.IsComplete);
    }

    [Theory]
    [InlineData(OneDriveSyncStatus.Unknown, "Unknown")]
    [InlineData(OneDriveSyncStatus.UpToDate, "UpToDate")]
    [InlineData(OneDriveSyncStatus.Syncing, "Syncing")]
    [InlineData(OneDriveSyncStatus.Paused, "Paused")]
    [InlineData(OneDriveSyncStatus.Error, "Error")]
    [InlineData(OneDriveSyncStatus.NotSignedIn, "NotSignedIn")]
    [InlineData(OneDriveSyncStatus.AuthenticationRequired, "AuthenticationRequired")]
    public void OneDriveSyncStatus_EnumValues_HaveCorrectNames(OneDriveSyncStatus status, string expectedName)
    {
        // Act & Assert
        Assert.Equal(expectedName, status.ToString());
    }

    [Theory]
    [InlineData(SyncFolderType.Personal, "Personal")]
    [InlineData(SyncFolderType.Business, "Business")]
    [InlineData(SyncFolderType.SharePointLibrary, "SharePointLibrary")]
    [InlineData(SyncFolderType.SharePointSite, "SharePointSite")]
    [InlineData(SyncFolderType.KnownFolder, "KnownFolder")]
    [InlineData(SyncFolderType.Other, "Other")]
    public void SyncFolderType_EnumValues_HaveCorrectNames(SyncFolderType type, string expectedName)
    {
        // Act & Assert
        Assert.Equal(expectedName, type.ToString());
    }

    [Fact]
    public void OneDriveStatus_LastChecked_IsSet()
    {
        // Arrange
        var beforeCreate = DateTime.UtcNow;
        var status = new OneDriveStatus
        {
            LastChecked = DateTime.UtcNow
        };
        var afterCreate = DateTime.UtcNow;

        // Act & Assert
        Assert.True(status.LastChecked >= beforeCreate);
        Assert.True(status.LastChecked <= afterCreate);
    }

    [Fact]
    public void OneDriveSyncError_Properties_SetCorrectly()
    {
        // Arrange & Act
        var error = new OneDriveSyncError
        {
            FilePath = @"C:\Users\Test\OneDrive\Document.docx",
            ErrorMessage = "File is locked by another process",
            ErrorCode = "0x80070020",
            ErrorTime = DateTime.UtcNow,
            IsRecoverable = true
        };

        // Assert
        Assert.Equal(@"C:\Users\Test\OneDrive\Document.docx", error.FilePath);
        Assert.Equal("File is locked by another process", error.ErrorMessage);
        Assert.Equal("0x80070020", error.ErrorCode);
        Assert.True(error.IsRecoverable);
    }

    [Fact]
    public void OneDriveSyncFolder_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var folder = new OneDriveSyncFolder();

        // Assert
        Assert.Equal(string.Empty, folder.LocalPath);
        Assert.Null(folder.RemotePath);
        Assert.Null(folder.DisplayName);
        Assert.Null(folder.SharePointSiteUrl);
        Assert.Null(folder.LibraryName);
        Assert.False(folder.IsSyncing);
        Assert.False(folder.HasErrors);
        Assert.Null(folder.SizeBytes);
        Assert.Null(folder.FileCount);
    }
}
