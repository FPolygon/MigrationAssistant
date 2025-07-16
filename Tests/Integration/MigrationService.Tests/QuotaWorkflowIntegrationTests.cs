using System.Runtime.Versioning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;
using MigrationTool.Service.ProfileManagement;
using Moq;
using Xunit;

namespace MigrationService.Tests.Integration;

[SupportedOSPlatform("windows")]
public class QuotaWorkflowIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IFileSystemService> _fileSystemServiceMock;
    private readonly Mock<IOneDriveDetector> _oneDriveDetectorMock;
    private readonly Mock<IUserProfileManager> _profileManagerMock;
    private readonly string _testDatabasePath;

    public QuotaWorkflowIntegrationTests()
    {
        var services = new ServiceCollection();

        // Setup test database
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"quota_integration_test_{Guid.NewGuid()}.db");
        var connectionString = $"Data Source={_testDatabasePath}";

        // Setup configuration
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"ServiceConfiguration:DatabaseConnectionString", connectionString},
                {"ServiceConfiguration:QuotaManagement:DefaultCompressionFactor", "0.7"},
                {"ServiceConfiguration:QuotaManagement:WarningThresholdPercentage", "80.0"},
                {"ServiceConfiguration:QuotaManagement:CriticalThresholdPercentage", "95.0"},
                {"ServiceConfiguration:QuotaManagement:MinimumFreeSpaceMB", "2048"},
                {"ServiceConfiguration:QuotaManagement:AutoEscalationEnabled", "true"},
                {"ServiceConfiguration:QuotaManagement:WarningCooldownHours", "24"},
                {"ServiceConfiguration:QuotaManagement:EscalationCooldownHours", "72"}
            })
            .Build();

        // Setup mocks
        _fileSystemServiceMock = new Mock<IFileSystemService>();
        _oneDriveDetectorMock = new Mock<IOneDriveDetector>();
        _profileManagerMock = new Mock<IUserProfileManager>();

        SetupMockDefaults();

        // Register services
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ILogger<StateManager>>(Mock.Of<ILogger<StateManager>>());
        services.AddSingleton<ILogger<BackupRequirementsCalculator>>(Mock.Of<ILogger<BackupRequirementsCalculator>>());
        services.AddSingleton<ILogger<OneDriveQuotaChecker>>(Mock.Of<ILogger<OneDriveQuotaChecker>>());
        services.AddSingleton<ILogger<QuotaWarningManager>>(Mock.Of<ILogger<QuotaWarningManager>>());
        services.AddSingleton<ILogger<OneDriveManager>>(Mock.Of<ILogger<OneDriveManager>>());

        services.AddSingleton<IStateManager, StateManager>();
        services.AddSingleton(_fileSystemServiceMock.Object);
        services.AddSingleton(_oneDriveDetectorMock.Object);
        services.AddSingleton(_profileManagerMock.Object);
        services.AddSingleton<IBackupRequirementsCalculator, BackupRequirementsCalculator>();
        services.AddSingleton<IOneDriveQuotaChecker, OneDriveQuotaChecker>();
        services.AddSingleton<IQuotaWarningManager, QuotaWarningManager>();

        // Mock other dependencies for OneDriveManager
        services.AddSingleton(Mock.Of<IOneDriveStatusCache>());
        services.AddSingleton(Mock.Of<IOneDriveRegistry>());
        services.AddSingleton(Mock.Of<IOneDriveProcessDetector>());
        services.AddSingleton(Mock.Of<IOneDriveSyncController>());
        services.AddSingleton<IOneDriveManager, OneDriveManager>();

        _serviceProvider = services.BuildServiceProvider();

        // Initialize database
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();
        Task.Run(async () => await stateManager.InitializeAsync()).Wait();
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        if (File.Exists(_testDatabasePath))
        {
            File.Delete(_testDatabasePath);
        }
    }

    [Fact]
    public async Task CompleteQuotaWorkflow_WithGoodQuotaHealth_CompletesWithoutWarnings()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid, profileSizeBytes: 1024 * 1024 * 1024); // 1GB
        var oneDriveStatus = CreateOneDriveStatus(userSid, totalMB: 10000, usedMB: 1000); // 10% usage

        SetupUserProfile(userProfile);
        SetupOneDriveStatus(userSid, oneDriveStatus);

        var requirementsCalculator = _serviceProvider.GetRequiredService<IBackupRequirementsCalculator>();
        var quotaChecker = _serviceProvider.GetRequiredService<IOneDriveQuotaChecker>();
        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Act - Execute complete quota workflow
        
        // Step 1: Calculate backup requirements
        var requirements = await requirementsCalculator.CalculateAsync(userSid);
        await stateManager.SaveBackupRequirementsAsync(requirements.ToRecord());

        // Step 2: Check quota health
        var quotaHealth = await quotaChecker.CheckQuotaHealthAsync(userSid);
        Assert.NotNull(quotaHealth);
        await stateManager.SaveQuotaStatusAsync(quotaHealth.ToRecord());

        // Step 3: Process warnings
        await warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Good, quotaHealth.HealthLevel);
        Assert.True(quotaHealth.CanAccommodateBackup);
        Assert.Equal(0, quotaHealth.ShortfallMB);

        var warnings = await stateManager.GetUnresolvedQuotaWarningsAsync(userSid);
        Assert.Empty(warnings);

        var escalations = await stateManager.GetQuotaEscalationsAsync(userSid);
        Assert.Empty(escalations);
    }

    [Fact]
    public async Task CompleteQuotaWorkflow_WithHighUsage_CreatesWarning()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid, profileSizeBytes: 2L * 1024 * 1024 * 1024); // 2GB
        var oneDriveStatus = CreateOneDriveStatus(userSid, totalMB: 10000, usedMB: 8500); // 85% usage

        SetupUserProfile(userProfile);
        SetupOneDriveStatus(userSid, oneDriveStatus);

        var requirementsCalculator = _serviceProvider.GetRequiredService<IBackupRequirementsCalculator>();
        var quotaChecker = _serviceProvider.GetRequiredService<IOneDriveQuotaChecker>();
        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Act
        var requirements = await requirementsCalculator.CalculateAsync(userSid);
        await stateManager.SaveBackupRequirementsAsync(requirements.ToRecord());

        var quotaHealth = await quotaChecker.CheckQuotaHealthAsync(userSid);
        Assert.NotNull(quotaHealth);
        await stateManager.SaveQuotaStatusAsync(quotaHealth.ToRecord());

        await warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Warning, quotaHealth.HealthLevel);
        Assert.True(quotaHealth.CanAccommodateBackup); // Still has space for backup

        var warnings = await stateManager.GetUnresolvedQuotaWarningsAsync(userSid);
        Assert.Single(warnings);
        Assert.Equal(QuotaWarningType.HighUsage, warnings[0].WarningType);
        Assert.Equal(QuotaWarningLevel.Warning, warnings[0].Level);
    }

    [Fact]
    public async Task CompleteQuotaWorkflow_WithInsufficientSpace_CreatesEscalation()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid, profileSizeBytes: 8L * 1024 * 1024 * 1024); // 8GB profile
        var oneDriveStatus = CreateOneDriveStatus(userSid, totalMB: 10000, usedMB: 7000); // 3GB available, but need ~5.6GB for backup

        SetupUserProfile(userProfile);
        SetupOneDriveStatus(userSid, oneDriveStatus);

        var requirementsCalculator = _serviceProvider.GetRequiredService<IBackupRequirementsCalculator>();
        var quotaChecker = _serviceProvider.GetRequiredService<IOneDriveQuotaChecker>();
        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Act
        var requirements = await requirementsCalculator.CalculateAsync(userSid);
        await stateManager.SaveBackupRequirementsAsync(requirements.ToRecord());

        var quotaHealth = await quotaChecker.CheckQuotaHealthAsync(userSid);
        Assert.NotNull(quotaHealth);
        await stateManager.SaveQuotaStatusAsync(quotaHealth.ToRecord());

        await warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        Assert.Equal(QuotaHealthLevel.Critical, quotaHealth.HealthLevel);
        Assert.False(quotaHealth.CanAccommodateBackup);
        Assert.True(quotaHealth.ShortfallMB > 0);

        var warnings = await stateManager.GetUnresolvedQuotaWarningsAsync(userSid);
        Assert.Single(warnings);
        Assert.Equal(QuotaWarningType.InsufficientBackupSpace, warnings[0].WarningType);
        Assert.Equal(QuotaWarningLevel.Critical, warnings[0].Level);

        var escalations = await stateManager.GetQuotaEscalationsAsync(userSid);
        Assert.Single(escalations);
        Assert.Equal(QuotaEscalationType.InsufficientSpace, escalations[0].EscalationType);
        Assert.Equal(EscalationPriority.Critical, escalations[0].Priority);
    }

    [Fact]
    public async Task QuotaWorkflow_WithRepeatedWarnings_CreatesEscalation()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid, profileSizeBytes: 1024 * 1024 * 1024);
        var oneDriveStatus = CreateOneDriveStatus(userSid, totalMB: 10000, usedMB: 8500); // High usage

        SetupUserProfile(userProfile);
        SetupOneDriveStatus(userSid, oneDriveStatus);

        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Create historical warnings (simulate 3 warnings in the past)
        var historicalWarnings = new List<QuotaWarning>();
        for (int i = 0; i < 3; i++)
        {
            var warning = new QuotaWarning
            {
                UserId = userSid,
                WarningType = QuotaWarningType.HighUsage,
                Level = QuotaWarningLevel.Warning,
                Title = $"Historical Warning {i + 1}",
                Message = "High usage detected",
                IsResolved = true,
                CreatedAt = DateTime.UtcNow.AddDays(-i - 1),
                ResolvedAt = DateTime.UtcNow.AddDays(-i - 1).AddHours(1),
                UpdatedAt = DateTime.UtcNow.AddDays(-i - 1).AddHours(1)
            };
            historicalWarnings.Add(warning);
            await stateManager.CreateQuotaWarningAsync(warning);
        }

        // Act - Process warnings again (should detect repeated pattern)
        await warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        var escalations = await stateManager.GetQuotaEscalationsAsync(userSid);
        Assert.Single(escalations);
        Assert.Equal(QuotaEscalationType.RepeatedWarnings, escalations[0].EscalationType);
        Assert.Contains("repeated", escalations[0].Description.ToLower());
    }

    [Fact]
    public async Task QuotaWorkflow_WithMultipleUsers_ProcessesAllCorrectly()
    {
        // Arrange
        var users = new[]
        {
            ("user1", 1024L * 1024 * 1024, 1000L), // Good quota
            ("user2", 2L * 1024 * 1024 * 1024, 8500L), // High usage warning
            ("user3", 8L * 1024 * 1024 * 1024, 7000L)  // Insufficient space escalation
        };

        var activeProfiles = new List<UserProfile>();
        
        foreach (var (userId, profileSize, usedMB) in users)
        {
            var profile = CreateTestUserProfile(userId, profileSize);
            var status = CreateOneDriveStatus(userId, 10000, usedMB);
            
            activeProfiles.Add(profile);
            SetupUserProfile(profile);
            SetupOneDriveStatus(userId, status);
        }

        _profileManagerMock.Setup(x => x.GetActiveProfilesRequiringBackupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(activeProfiles);

        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Act
        await warningManager.ProcessAllUsersWarningsAsync();

        // Assert
        
        // User1 should have no warnings (good quota)
        var user1Warnings = await stateManager.GetUnresolvedQuotaWarningsAsync("user1");
        Assert.Empty(user1Warnings);

        // User2 should have high usage warning
        var user2Warnings = await stateManager.GetUnresolvedQuotaWarningsAsync("user2");
        Assert.Single(user2Warnings);
        Assert.Equal(QuotaWarningType.HighUsage, user2Warnings[0].WarningType);

        // User3 should have insufficient backup space warning and escalation
        var user3Warnings = await stateManager.GetUnresolvedQuotaWarningsAsync("user3");
        Assert.Single(user3Warnings);
        Assert.Equal(QuotaWarningType.InsufficientBackupSpace, user3Warnings[0].WarningType);

        var user3Escalations = await stateManager.GetQuotaEscalationsAsync("user3");
        Assert.Single(user3Escalations);
        Assert.Equal(QuotaEscalationType.InsufficientSpace, user3Escalations[0].EscalationType);
    }

    [Fact]
    public async Task QuotaWorkflow_WithOneDriveNotConfigured_HandlesGracefully()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        var userProfile = CreateTestUserProfile(userSid, profileSizeBytes: 1024 * 1024 * 1024);

        SetupUserProfile(userProfile);
        // Don't setup OneDrive status (simulate OneDrive not configured)
        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OneDriveStatus?)null);

        var quotaChecker = _serviceProvider.GetRequiredService<IOneDriveQuotaChecker>();
        var warningManager = _serviceProvider.GetRequiredService<IQuotaWarningManager>();
        var stateManager = _serviceProvider.GetRequiredService<IStateManager>();

        // Act
        var quotaHealth = await quotaChecker.CheckQuotaHealthAsync(userSid);
        await warningManager.ProcessUserWarningsAsync(userSid);

        // Assert
        Assert.Null(quotaHealth); // Should return null for unconfigured OneDrive
        
        var warnings = await stateManager.GetUnresolvedQuotaWarningsAsync(userSid);
        Assert.Empty(warnings); // Should not create warnings for unconfigured OneDrive

        var escalations = await stateManager.GetQuotaEscalationsAsync(userSid);
        Assert.Empty(escalations); // Should not create escalations
    }

    private void SetupMockDefaults()
    {
        _fileSystemServiceMock.Setup(x => x.GetDirectorySizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100 * 1024 * 1024); // 100MB per directory

        _fileSystemServiceMock.Setup(x => x.DirectoryExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private void SetupUserProfile(UserProfile profile)
    {
        _profileManagerMock.Setup(x => x.GetProfileAsync(profile.UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(profile);
    }

    private void SetupOneDriveStatus(string userSid, OneDriveStatus status)
    {
        _oneDriveDetectorMock.Setup(x => x.GetOneDriveQuotaAsync(userSid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
    }

    private UserProfile CreateTestUserProfile(string userSid, long profileSizeBytes)
    {
        return new UserProfile
        {
            UserId = userSid,
            UserName = $"testuser-{userSid.Split('-').Last()}",
            ProfilePath = $@"C:\Users\testuser-{userSid.Split('-').Last()}",
            ProfileSizeBytes = profileSizeBytes,
            IsActive = true
        };
    }

    private OneDriveStatus CreateOneDriveStatus(string userSid, long totalMB, long usedMB)
    {
        return new OneDriveStatus
        {
            UserId = userSid,
            SyncStatus = "UpToDate",
            QuotaTotalMB = totalMB,
            QuotaUsedMB = usedMB,
            QuotaAvailableMB = totalMB - usedMB,
            IsSignedIn = true,
            LastUpdated = DateTime.UtcNow
        };
    }
}