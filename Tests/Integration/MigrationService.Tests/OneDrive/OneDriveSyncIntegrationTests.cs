using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.Integration.OneDrive;

[SupportedOSPlatform("windows")]
public class OneDriveSyncIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IProcessService> _processServiceMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly IOneDriveManager _manager;
    private readonly IOneDriveSyncController _syncController;

    public OneDriveSyncIntegrationTests()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        // Add mocks
        _registryMock = new Mock<IOneDriveRegistry>();
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _processServiceMock = new Mock<IProcessService>();
        _stateManagerMock = new Mock<IStateManager>();

        services.AddSingleton(_registryMock.Object);
        services.AddSingleton(_fileSystemServiceMock.Object);
        services.AddSingleton(_processServiceMock.Object);
        services.AddSingleton(_stateManagerMock.Object);

        // Add real implementations
        services.AddSingleton<IOneDriveProcessDetector, OneDriveProcessDetector>();
        services.AddSingleton<IOneDriveDetector, OneDriveDetector>();
        services.AddSingleton<IOneDriveStatusCache, OneDriveStatusCache>();
        services.AddSingleton<IOneDriveManager, OneDriveManager>();
        services.AddSingleton<IOneDriveSyncController, OneDriveSyncController>();

        _serviceProvider = services.BuildServiceProvider();
        _manager = _serviceProvider.GetRequiredService<IOneDriveManager>();
        _syncController = _serviceProvider.GetRequiredService<IOneDriveSyncController>();
    }

    [Fact]
    public async Task FullSyncWorkflow_LocalFilesToCloud_CompletesSuccessfully()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var syncFolder = @"C:\Users\TestUser\OneDrive - Contoso";
        var targetFolder = @"C:\Users\TestUser\OneDrive - Contoso\Documents\BackupData";

        SetupBasicOneDriveEnvironment(userSid, accountId, syncFolder);
        SetupLocalOnlyFiles(targetFolder);

        // Setup state manager for sync operations
        var syncOperation = new SyncOperation
        {
            Id = 1,
            UserSid = userSid,
            FolderPath = targetFolder,
            Status = SyncOperationStatus.Pending
        };

        _stateManagerMock.Setup(s => s.CreateSyncOperationAsync(It.IsAny<SyncOperation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        _stateManagerMock.Setup(s => s.GetActiveSyncOperationAsync(userSid, targetFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncOperation);

        // Act & Assert - Step 1: Check initial status
        var status = await _manager.GetStatusAsync(userSid);
        Assert.True(status.IsSignedIn);
        Assert.NotNull(status.AccountInfo);

        // Step 2: Ensure folder is in sync scope
        var inScope = await _syncController.IsFolderInSyncScopeAsync(userSid, targetFolder);
        if (!inScope)
        {
            var added = await _syncController.AddFolderToSyncScopeAsync(userSid, accountId, targetFolder);
            Assert.True(added);
        }

        // Step 3: Force sync to start
        await _manager.ForceSyncAsync(targetFolder);

        // Step 4: Monitor sync progress (simplified for test)
        var progress = await _manager.GetSyncProgressAsync(targetFolder);
        Assert.Equal(OneDriveSyncStatus.Syncing, progress.Status);
        Assert.True(progress.TotalFiles > 0);

        // Step 5: Handle any sync errors
        _stateManagerMock.Setup(s => s.GetUnresolvedSyncErrorsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SyncError>());

        var errorsResolved = await _manager.TryResolveSyncErrorsAsync(userSid);
        Assert.True(errorsResolved);
    }

    [Fact]
    public async Task SyncErrorHandling_WithRetryLogic_EscalatesToIT()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var syncFolder = @"C:\Users\TestUser\OneDrive - Contoso";

        SetupBasicOneDriveEnvironment(userSid, accountId, syncFolder);
        SetupOneDriveWithErrors(userSid);

        // Setup persistent sync errors
        var syncErrors = new List<SyncError>
        {
            new SyncError 
            { 
                Id = 1,
                FilePath = @"C:\Users\TestUser\OneDrive - Contoso\locked_file.xlsx",
                ErrorMessage = "File is locked by another process",
                RetryAttempts = 3,
                EscalatedToIT = false
            },
            new SyncError 
            { 
                Id = 2,
                FilePath = @"C:\Users\TestUser\OneDrive - Contoso\invalid|name.txt",
                ErrorMessage = "Invalid path characters",
                RetryAttempts = 3,
                EscalatedToIT = false
            }
        };

        _stateManagerMock.Setup(s => s.GetUnresolvedSyncErrorsAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncErrors);

        var syncOperation = new SyncOperation
        {
            Id = 1,
            UserSid = userSid,
            FolderPath = syncFolder,
            Status = SyncOperationStatus.InProgress,
            ErrorCount = 2
        };

        _stateManagerMock.Setup(s => s.GetActiveSyncOperationAsync(userSid, syncFolder, It.IsAny<CancellationToken>()))
            .ReturnsAsync(syncOperation);

        ITEscalation? capturedEscalation = null;
        _stateManagerMock.Setup(s => s.CreateEscalationAsync(It.IsAny<ITEscalation>(), It.IsAny<CancellationToken>()))
            .Callback<ITEscalation, CancellationToken>((e, ct) => capturedEscalation = e)
            .ReturnsAsync(1);

        // Act
        var result = await _manager.TryResolveSyncErrorsAsync(userSid);

        // Assert
        Assert.False(result); // Errors were not resolved
        Assert.NotNull(capturedEscalation);
        Assert.Equal(userSid, capturedEscalation.UserId);
        Assert.Contains("sync errors", capturedEscalation.Reason);
        Assert.Contains("locked_file.xlsx", capturedEscalation.Details);
        Assert.Contains("invalid|name.txt", capturedEscalation.Details);
    }

    [Fact]
    public async Task SelectiveSyncManagement_CriticalFolders_EnsuresInclusion()
    {
        // Arrange
        var userSid = "S-1-5-21-123";
        var accountId = "Business1";
        var syncFolder = @"C:\Users\TestUser\OneDrive - Contoso";
        var criticalFolders = new List<string>
        {
            @"C:\Users\TestUser\OneDrive - Contoso\Documents\ImportantDocs",
            @"C:\Users\TestUser\OneDrive - Contoso\Desktop\WorkFiles",
            @"C:\Users\TestUser\OneDrive - Contoso\Pictures\ProjectPhotos"
        };

        SetupBasicOneDriveEnvironment(userSid, accountId, syncFolder);

        // Setup some folders as excluded
        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "ExcludedFolders"))
            .ReturnsAsync(new string[] { @"Documents\ImportantDocs", @"Pictures" });

        foreach (var folder in criticalFolders)
        {
            _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folder))
                .ReturnsAsync(true);
        }

        // Act
        var results = await _syncController.EnsureCriticalFoldersIncludedAsync(userSid, accountId, criticalFolders);

        // Assert
        Assert.Equal(3, results.Count);
        foreach (var folder in criticalFolders)
        {
            Assert.True(results[folder], $"Critical folder {folder} should be included in sync");
        }
    }

    private void SetupBasicOneDriveEnvironment(string userSid, string accountId, string syncFolder)
    {
        // Setup OneDrive as installed and running
        _processServiceMock.Setup(p => p.GetProcessesByNameAsync("OneDrive"))
            .ReturnsAsync(new[] { Mock.Of<IProcessInfo>(p => p.ProcessName == "OneDrive" && p.Id == 1234) });

        // Setup registry values
        _registryMock.Setup(r => r.GetLocalMachineRegistryValueAsync(
            @"SOFTWARE\Microsoft\OneDrive",
            "CurrentVersionPath"))
            .ReturnsAsync(@"C:\Program Files\Microsoft OneDrive\OneDrive.exe");

        // Setup user profile
        _stateManagerMock.Setup(s => s.GetUserProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new UserProfile { UserId = userSid, UserName = "TestUser" } });

        // Setup OneDrive account info
        var accountInfo = new OneDriveAccountInfo
        {
            AccountId = accountId,
            Email = "test@contoso.com",
            UserFolder = syncFolder,
            IsPrimary = true,
            SyncStatus = OneDriveSyncStatus.UpToDate,
            SyncedFolders = new List<SyncedFolder>
            {
                new SyncedFolder { LocalPath = syncFolder, LibraryType = LibraryType.Personal }
            }
        };

        _registryMock.Setup(r => r.GetUserAccountsAsync(userSid, null))
            .ReturnsAsync(new List<OneDriveAccountInfo> { accountInfo });

        _registryMock.Setup(r => r.GetSyncedFoldersAsync(userSid, null))
            .ReturnsAsync(accountInfo.SyncedFolders);

        _registryMock.Setup(r => r.GetUserRegistryValueAsync(
            userSid,
            $@"SOFTWARE\Microsoft\OneDrive\Accounts\{accountId}",
            "UserFolder"))
            .ReturnsAsync(syncFolder);

        // Setup file system
        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(syncFolder))
            .ReturnsAsync(true);
    }

    private void SetupLocalOnlyFiles(string folder)
    {
        var files = new[]
        {
            CreateMockFileInfo($@"{folder}\document1.docx", 1024 * 10, FileAttributes.Normal),
            CreateMockFileInfo($@"{folder}\spreadsheet.xlsx", 1024 * 50, FileAttributes.Normal),
            CreateMockFileInfo($@"{folder}\presentation.pptx", 1024 * 100, FileAttributes.Normal)
        };

        _fileSystemServiceMock.Setup(f => f.DirectoryExistsAsync(folder))
            .ReturnsAsync(true);
        _fileSystemServiceMock.Setup(f => f.GetFilesAsync(folder, "*", SearchOption.AllDirectories))
            .ReturnsAsync(files);

        foreach (var file in files)
        {
            _fileSystemServiceMock.Setup(f => f.GetFileInfoAsync(file.Object.FullName))
                .ReturnsAsync(file.Object);
        }
    }

    private void SetupOneDriveWithErrors(string userSid)
    {
        var status = new OneDriveStatus
        {
            IsInstalled = true,
            IsRunning = true,
            IsSignedIn = true,
            AccountEmail = "test@contoso.com",
            SyncStatus = OneDriveSyncStatus.Error,
            SyncFolder = @"C:\Users\TestUser\OneDrive - Contoso",
            AccountInfo = new OneDriveAccountInfo
            {
                Email = "test@contoso.com",
                SyncStatus = OneDriveSyncStatus.Error,
                HasSyncErrors = true,
                SyncedFolders = new List<SyncedFolder>
                {
                    new SyncedFolder 
                    { 
                        LocalPath = @"C:\Users\TestUser\OneDrive - Contoso",
                        HasErrors = true
                    }
                }
            }
        };

        var detector = _serviceProvider.GetRequiredService<IOneDriveDetector>();
        var detectorMock = Mock.Get(detector);
        if (detectorMock != null)
        {
            detectorMock.Setup(d => d.DetectOneDriveStatusAsync(userSid, It.IsAny<CancellationToken>()))
                .ReturnsAsync(status);
        }
    }

    private Mock<IFileInfo> CreateMockFileInfo(string path, long size, FileAttributes attributes)
    {
        var fileInfo = new Mock<IFileInfo>();
        fileInfo.Setup(f => f.FullName).Returns(path);
        fileInfo.Setup(f => f.Name).Returns(Path.GetFileName(path));
        fileInfo.Setup(f => f.Exists).Returns(true);
        fileInfo.Setup(f => f.Length).Returns(size);
        fileInfo.Setup(f => f.Attributes).Returns(attributes);
        fileInfo.Setup(f => f.LastWriteTimeUtc).Returns(DateTime.UtcNow.AddMinutes(-5));
        return fileInfo;
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
    }
}