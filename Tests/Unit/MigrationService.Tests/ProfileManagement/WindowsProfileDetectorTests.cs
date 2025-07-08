using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using MigrationTool.Service.ProfileManagement.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class WindowsProfileDetectorTests
{
    private readonly Mock<ILogger<WindowsProfileDetector>> _loggerMock;
    private readonly Mock<IWindowsProfileRegistry> _profileRegistryMock;
    private readonly WindowsProfileDetector _detector;

    public WindowsProfileDetectorTests()
    {
        _loggerMock = new Mock<ILogger<WindowsProfileDetector>>();
        _profileRegistryMock = new Mock<IWindowsProfileRegistry>();
        _detector = new WindowsProfileDetector(_loggerMock.Object, _profileRegistryMock.Object);
    }

    [Fact]
    public async Task DiscoverProfilesAsync_ReturnsEmptyList_WhenNoProfilesFound()
    {
        // Arrange
        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(new List<ProfileRegistryInfo>());

        // Act
        var result = await _detector.DiscoverProfilesAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().BeEmpty();
        _profileRegistryMock.Verify(x => x.EnumerateProfiles(), Times.Once);
    }

    [Fact]
    public async Task DiscoverProfilesAsync_FiltersSystemAccounts_WhenIncludeSystemAccountsIsFalse()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1", isSystem: false),
            CreateProfileInfo("S-1-5-18", "SYSTEM", @"C:\Windows\System32\config\systemprofile", isSystem: true),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1002", "user2", @"C:\Users\user2", isSystem: false)
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ProfileAccountType.Local);

        // Act
        var result = await _detector.DiscoverProfilesAsync(includeSystemAccounts: false);

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(p => p.UserId == "S-1-5-18");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1001");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1002");
    }

    [Fact]
    public async Task DiscoverProfilesAsync_IncludesSystemAccounts_WhenIncludeSystemAccountsIsTrue()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1", isSystem: false),
            CreateProfileInfo("S-1-5-18", "SYSTEM", @"C:\Windows\System32\config\systemprofile", isSystem: true)
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string sid, string? userName) => sid == "S-1-5-18" ? ProfileAccountType.System : ProfileAccountType.Local);

        // Act
        var result = await _detector.DiscoverProfilesAsync(includeSystemAccounts: true);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(p => p.UserId == "S-1-5-18");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1001");
    }

    [Fact]
    public async Task DiscoverProfilesAsync_SkipsCorruptedProfiles()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1", isCorrupted: false),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1002", "user2", @"C:\Users\user2", isCorrupted: true),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1003", "user3", @"C:\Users\user3", isCorrupted: false)
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ProfileAccountType.Local);

        // Act
        var result = await _detector.DiscoverProfilesAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(p => p.UserId == "S-1-5-21-1234-5678-9012-1002");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1001");
        result.Should().Contain(p => p.UserId == "S-1-5-21-1234-5678-9012-1003");
    }

    [Fact]
    public async Task DiscoverProfilesAsync_MapsAccountTypesCorrectly()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1"),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1002", @"DOMAIN\user2", @"C:\Users\user2"),
            CreateProfileInfo("S-1-12-1-1234-5678-9012-3456", "azureuser", @"C:\Users\azureuser")
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType("S-1-5-21-1234-5678-9012-1001", "user1"))
            .Returns(ProfileAccountType.Local);
        
        _profileRegistryMock
            .Setup(x => x.GetAccountType("S-1-5-21-1234-5678-9012-1002", @"DOMAIN\user2"))
            .Returns(ProfileAccountType.Domain);
        
        _profileRegistryMock
            .Setup(x => x.GetAccountType("S-1-12-1-1234-5678-9012-3456", "azureuser"))
            .Returns(ProfileAccountType.AzureAD);

        // Act
        var result = await _detector.DiscoverProfilesAsync();

        // Assert
        result.Should().HaveCount(3);
        result.First(p => p.UserId == "S-1-5-21-1234-5678-9012-1001").ProfileType.Should().Be(ProfileType.Local);
        result.First(p => p.UserId == "S-1-5-21-1234-5678-9012-1002").ProfileType.Should().Be(ProfileType.Domain);
        result.First(p => p.UserId == "S-1-12-1-1234-5678-9012-3456").ProfileType.Should().Be(ProfileType.AzureAD);
    }

    [Fact]
    public async Task DiscoverProfilesAsync_ExtractsDomainFromUsername()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", @"CONTOSO\john.doe", @"C:\Users\john.doe")
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ProfileAccountType.Domain);

        // Act
        var result = await _detector.DiscoverProfilesAsync();

        // Assert
        result.Should().HaveCount(1);
        var profile = result.First();
        profile.UserName.Should().Be("john.doe");
        profile.DomainName.Should().Be("CONTOSO");
    }

    [Fact]
    public async Task GetProfileBySidAsync_ReturnsProfile_WhenProfileExists()
    {
        // Arrange
        var targetSid = "S-1-5-21-1234-5678-9012-1001";
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo(targetSid, "user1", @"C:\Users\user1"),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1002", "user2", @"C:\Users\user2")
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(ProfileAccountType.Local);

        // Act
        var result = await _detector.GetProfileBySidAsync(targetSid);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(targetSid);
        result.UserName.Should().Be("user1");
    }

    [Fact]
    public async Task GetProfileBySidAsync_ReturnsNull_WhenProfileDoesNotExist()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1")
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        // Act
        var result = await _detector.GetProfileBySidAsync("S-1-5-21-9999-9999-9999-9999");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void IsProfileAccessible_ReturnsTrue_WhenDirectoryExistsAndAccessible()
    {
        // Arrange
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempPath);

        try
        {
            // Act
            var result = _detector.IsProfileAccessible(tempPath);

            // Assert
            result.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            Directory.Delete(tempPath);
        }
    }

    [Fact]
    public void IsProfileAccessible_ReturnsFalse_WhenDirectoryDoesNotExist()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        // Act
        var result = _detector.IsProfileAccessible(nonExistentPath);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DiscoverProfilesAsync_SetsRequiresBackupCorrectly()
    {
        // Arrange
        var profiles = new List<ProfileRegistryInfo>
        {
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1001", "user1", @"C:\Users\user1", isSystem: false, isTemporary: false),
            CreateProfileInfo("S-1-5-18", "SYSTEM", @"C:\Windows\System32\config\systemprofile", isSystem: true),
            CreateProfileInfo("S-1-5-21-1234-5678-9012-1002", "user2", @"C:\Users\user2.TEMP", isSystem: false, isTemporary: true)
        };

        _profileRegistryMock
            .Setup(x => x.EnumerateProfiles())
            .Returns(profiles);

        _profileRegistryMock
            .Setup(x => x.GetAccountType(It.IsAny<string>(), It.IsAny<string>()))
            .Returns((string sid, string? userName) => sid == "S-1-5-18" ? ProfileAccountType.System : ProfileAccountType.Local);

        // Act
        var result = await _detector.DiscoverProfilesAsync(includeSystemAccounts: true);

        // Assert
        result.Should().HaveCount(3);
        result.First(p => p.UserId == "S-1-5-21-1234-5678-9012-1001").RequiresBackup.Should().BeTrue();
        result.First(p => p.UserId == "S-1-5-18").RequiresBackup.Should().BeFalse();
        result.First(p => p.UserId == "S-1-5-21-1234-5678-9012-1002").RequiresBackup.Should().BeFalse();
    }

    private static ProfileRegistryInfo CreateProfileInfo(
        string sid, 
        string userName, 
        string profilePath,
        bool isSystem = false,
        bool isTemporary = false,
        bool isCorrupted = false)
    {
        return new ProfileRegistryInfo
        {
            Sid = sid,
            UserName = userName,
            ProfilePath = profilePath,
            IsSystemAccount = isSystem,
            IsTemporary = isTemporary,
            IsCorrupted = isCorrupted,
            State = ProfileState.None,
            Flags = ProfileFlags.None,
            LastLoadTime = DateTime.UtcNow.AddDays(-5)
        };
    }
}