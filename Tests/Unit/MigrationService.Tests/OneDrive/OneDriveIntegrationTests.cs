using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationService.Tests.OneDrive.TestUtilities;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.OneDrive;

/// <summary>
/// Integration tests for OneDrive functionality focusing on component interaction
/// and caching behavior
/// </summary>
[Collection("Database")]
public class OneDriveIntegrationTests
{
    private readonly Mock<ILogger<OneDriveManager>> _managerLoggerMock;
    private readonly Mock<ILogger<OneDriveDetector>> _detectorLoggerMock;
    private readonly Mock<ILogger<OneDriveProcessDetector>> _processLoggerMock;
    private readonly Mock<ILogger<OneDriveStatusCache>> _cacheLoggerMock;
    private readonly Mock<IStateManager> _stateManagerMock;
    private readonly Mock<IOneDriveRegistry> _registryMock;
    private readonly TestFileSystemService _testFileSystemService;
    private readonly TestProcessService _testProcessService;
    private readonly OneDriveManager _manager;
    private readonly OneDriveStatusCache _cache;
    private readonly string _testUserSid = "S-1-5-21-123456789-1234567890-1234567890-1001";

    public OneDriveIntegrationTests()
    {
        _managerLoggerMock = new Mock<ILogger<OneDriveManager>>();
        _detectorLoggerMock = new Mock<ILogger<OneDriveDetector>>();
        _processLoggerMock = new Mock<ILogger<OneDriveProcessDetector>>();
        _cacheLoggerMock = new Mock<ILogger<OneDriveStatusCache>>();
        _stateManagerMock = new Mock<IStateManager>();
        _registryMock = new Mock<IOneDriveRegistry>();

        // Initialize test services
        _testFileSystemService = new TestFileSystemService();
        _testProcessService = new TestProcessService();

        // Create real components with mocked dependencies
        IOneDriveProcessDetector processDetector = new OneDriveProcessDetector(_processLoggerMock.Object, _testProcessService);
        IOneDriveDetector detector = new OneDriveDetector(_detectorLoggerMock.Object, _registryMock.Object, processDetector, _testFileSystemService);
        _cache = new OneDriveStatusCache(_cacheLoggerMock.Object, TimeSpan.FromMinutes(5));

        _manager = new OneDriveManager(
            _managerLoggerMock.Object,
            detector,
            _cache,
            _registryMock.Object,
            processDetector,
            _stateManagerMock.Object,
            _testFileSystemService);
    }

    [Fact]
    public async Task GetStatusAsync_FullIntegration_DetectsOneDriveCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso",
                IsPrimary = true,
                UsedSpaceBytes = 1073741824,
                TotalSpaceBytes = 5368709120
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, _testUserSid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);
        _registryMock.Setup(r => r.IsSyncPausedAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(false);

        // Act
        var status = await _manager.GetStatusAsync(_testUserSid);

        // Assert
        status.Should().NotBeNull();
        status.IsInstalled.Should().BeTrue();
        status.IsSignedIn.Should().BeTrue();
        status.AccountEmail.Should().Be("user@contoso.com");
        status.SyncFolder.Should().Be(@"C:\Users\TestUser\OneDrive - Contoso");
        status.AccountInfo.Should().NotBeNull();
        status.AccountInfo!.AvailableSpaceMB.Should().Be(4096);
    }

    [Fact]
    public async Task GetStatusAsync_WithKnownFolderMove_DetectsKfmCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso",
                IsPrimary = true
            }
        };

        var kfmStatus = new MigrationTool.Service.OneDrive.Models.KnownFolderMoveStatus
        {
            IsEnabled = true,
            DesktopRedirected = true,
            DesktopPath = @"C:\Users\TestUser\OneDrive - Contoso\Desktop",
            DocumentsRedirected = true,
            DocumentsPath = @"C:\Users\TestUser\OneDrive - Contoso\Documents",
            PicturesRedirected = false
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, _testUserSid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);
        _registryMock.Setup(r => r.GetKnownFolderMoveStatusAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(kfmStatus);

        // Act
        var status = await _manager.GetStatusAsync(_testUserSid);

        // Assert
        status.AccountInfo?.KfmStatus.Should().NotBeNull();
        status.AccountInfo!.KfmStatus!.IsEnabled.Should().BeTrue();
        status.AccountInfo.KfmStatus.RedirectedFolderCount.Should().Be(2);
        status.AccountInfo.KfmStatus.GetRedirectedFolders().Should().Contain("Desktop");
        status.AccountInfo.KfmStatus.GetRedirectedFolders().Should().Contain("Documents");
        status.AccountInfo.KfmStatus.GetRedirectedFolders().Should().NotContain("Pictures");
    }

    [Fact]
    public async Task GetStatusAsync_WithSyncErrors_DetectsErrorsCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso",
                IsPrimary = true,
                HasSyncErrors = true,
                SyncErrorDetails = "Authentication token expired"
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        // Don't set process running to simulate authentication issues
        _testProcessService.ClearAllProcesses();

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);

        // Act
        var status = await _manager.GetStatusAsync(_testUserSid);

        // Assert
        status.SyncStatus.Should().Be(MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.AuthenticationRequired);
        status.AccountInfo!.HasSyncErrors.Should().BeTrue();
        status.AccountInfo.SyncErrorDetails.Should().Contain("Authentication token expired");
    }

    [Fact]
    public async Task GetStatusAsync_CachingBehavior_CachesCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso"
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, _testUserSid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);

        // Act - First call (should hit registry and cache result)
        var status1 = await _manager.GetStatusAsync(_testUserSid);

        // Assert - Check cache
        var cachedStatus = _cache.GetCachedStatus(_testUserSid);
        cachedStatus.Should().NotBeNull();
        cachedStatus!.AccountEmail.Should().Be("user@contoso.com");

        // Act - Second call (should use cache)
        var status2 = await _manager.GetStatusAsync(_testUserSid);

        // Assert
        status1.Should().NotBeNull();
        status2.Should().NotBeNull();
        status1.LastChecked.Should().Be(status2.LastChecked); // Same timestamp = cached
        status1.AccountEmail.Should().Be(status2.AccountEmail);

        // Verify registry was called only once
        _registryMock.Verify(r => r.GetUserAccountsAsync(_testUserSid, null), Times.Once);
    }

    [Fact]
    public async Task GetAvailableSpaceMBAsync_CalculatesCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso",
                IsPrimary = true,
                UsedSpaceBytes = 1073741824, // 1GB
                TotalSpaceBytes = 5368709120 // 5GB
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, _testUserSid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);

        // Act
        var availableSpace = await _manager.GetAvailableSpaceMBAsync(_testUserSid);

        // Assert
        availableSpace.Should().Be(4096); // 4GB available (5GB - 1GB)
    }

    [Fact]
    public async Task GetAvailableSpaceMBAsync_WhenNotSignedIn_ReturnsNegativeOne()
    {
        // Arrange
        // Configure test services for not signed in scenario
        _testProcessService.ClearAllProcesses();

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(false);

        // Act
        var availableSpace = await _manager.GetAvailableSpaceMBAsync(_testUserSid);

        // Assert
        availableSpace.Should().Be(-1);
    }

    [Fact]
    public async Task MultiUserScenario_CachesUsersIndependently()
    {
        // Arrange
        var user1Sid = "S-1-5-21-111111111-1111111111-1111111111-1001";
        var user2Sid = "S-1-5-21-222222222-2222222222-2222222222-1002";

        var user1Accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo {
                AccountId = "Business1",
                Email = "user1@contoso.com",
                UserFolder = @"C:\Users\User1\OneDrive - Contoso"
            }
        };

        var user2Accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo {
                AccountId = "Business1",
                Email = "user2@contoso.com",
                UserFolder = @"C:\Users\User2\OneDrive - Contoso",
                HasSyncErrors = true
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\User1\OneDrive - Contoso", true);
        _testFileSystemService.SetDirectoryExists(@"C:\Users\User2\OneDrive - Contoso", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, user1Sid);
        _testProcessService.SetProcessRunning("OneDrive", 5678, user2Sid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(user1Sid, It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(user1Accounts);
        _registryMock.Setup(r => r.GetUserAccountsAsync(user2Sid, It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(user2Accounts);

        // Act
        var status1 = await _manager.GetStatusAsync(user1Sid);
        var status2 = await _manager.GetStatusAsync(user2Sid);

        // Assert - Status results
        status1.AccountEmail.Should().Be("user1@contoso.com");
        status1.SyncStatus.Should().Be(MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.UpToDate);

        status2.AccountEmail.Should().Be("user2@contoso.com");
        status2.SyncStatus.Should().Be(MigrationTool.Service.OneDrive.Models.OneDriveSyncStatus.AuthenticationRequired);

        // Assert - Cache behavior
        var cached1 = _cache.GetCachedStatus(user1Sid);
        var cached2 = _cache.GetCachedStatus(user2Sid);
        cached1.Should().NotBeNull();
        cached2.Should().NotBeNull();
        cached1!.AccountEmail.Should().Be("user1@contoso.com");
        cached2!.AccountEmail.Should().Be("user2@contoso.com");
    }

    [Fact]
    public async Task GetStatusAsync_WithSharePointLibraries_DetectsCorrectly()
    {
        // Arrange
        var accounts = new List<OneDriveAccountInfo>
        {
            new OneDriveAccountInfo
            {
                AccountId = "Business1",
                Email = "user@contoso.com",
                UserFolder = @"C:\Users\TestUser\OneDrive - Contoso",
                IsPrimary = true
            }
        };

        var syncedFolders = new List<OneDriveSyncFolder>
        {
            new OneDriveSyncFolder
            {
                LocalPath = @"C:\Users\TestUser\OneDrive - Contoso",
                FolderType = SyncFolderType.Business,
                DisplayName = "OneDrive - Contoso"
            },
            new OneDriveSyncFolder
            {
                LocalPath = @"C:\Users\TestUser\SharePoint - Project Documents",
                FolderType = SyncFolderType.SharePointLibrary,
                DisplayName = "Project Documents - Team Site",
                SharePointSiteUrl = "https://contoso.sharepoint.com/sites/TeamSite",
                LibraryName = "Project Documents"
            }
        };

        // Configure test services
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\OneDrive - Contoso", true);
        _testFileSystemService.SetDirectoryExists(@"C:\Users\TestUser\SharePoint - Project Documents", true);
        _testProcessService.SetProcessRunning("OneDrive", 1234, _testUserSid);

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(accounts);
        _registryMock.Setup(r => r.GetSyncedFoldersAsync(It.IsAny<string>(), It.IsAny<Microsoft.Win32.RegistryKey?>())).ReturnsAsync(syncedFolders);

        // Act
        var status = await _manager.GetStatusAsync(_testUserSid);

        // Assert
        status.AccountInfo!.SyncedFolders.Should().HaveCount(2);
        var spFolder = status.AccountInfo.SyncedFolders.Find(f => f.FolderType == SyncFolderType.SharePointLibrary);
        spFolder.Should().NotBeNull();
        spFolder!.SharePointSiteUrl.Should().Be("https://contoso.sharepoint.com/sites/TeamSite");
        spFolder.LibraryName.Should().Be("Project Documents");
    }

    [Fact]
    public async Task TryRecoverAuthenticationAsync_LogsAttempt()
    {
        // Arrange
        // Configure test services for authentication recovery scenario
        _testProcessService.ClearAllProcesses();

        _registryMock.Setup(r => r.IsOneDriveInstalled()).Returns(true);
        _registryMock.Setup(r => r.GetUserAccountsAsync(It.IsAny<string>(), null))
            .ReturnsAsync(new List<OneDriveAccountInfo>());

        // Act
        var result = await _manager.TryRecoverAuthenticationAsync(_testUserSid);

        // Assert
        result.Should().BeFalse(); // Not implemented yet

        // Verify logging occurred
        _managerLoggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Attempting to recover OneDrive authentication")),
            It.IsAny<Exception>(),
            It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }
}
