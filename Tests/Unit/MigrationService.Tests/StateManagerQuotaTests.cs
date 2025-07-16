using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using Moq;
using System.Data;
using Xunit;

namespace MigrationService.Tests;

public class StateManagerQuotaTests : IDisposable
{
    private readonly Mock<ILogger<StateManager>> _loggerMock;
    private readonly Mock<IConfiguration> _configurationMock;
    private readonly StateManager _stateManager;
    private readonly string _connectionString;
    private readonly string _testDatabasePath;

    public StateManagerQuotaTests()
    {
        _loggerMock = new Mock<ILogger<StateManager>>();
        _configurationMock = new Mock<IConfiguration>();

        // Create a unique test database for each test
        _testDatabasePath = Path.Combine(Path.GetTempPath(), $"test_quota_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_testDatabasePath}";

        var connectionStringSection = new Mock<IConfigurationSection>();
        connectionStringSection.Setup(x => x.Value).Returns(_connectionString);
        _configurationMock.Setup(x => x.GetSection("ServiceConfiguration:DatabaseConnectionString"))
            .Returns(connectionStringSection.Object);

        _stateManager = new StateManager(_loggerMock.Object, _configurationMock.Object);

        // Initialize the database with migrations
        Task.Run(async () => await _stateManager.InitializeAsync()).Wait();
    }

    public void Dispose()
    {
        _stateManager?.Dispose();
        if (File.Exists(_testDatabasePath))
        {
            File.Delete(_testDatabasePath);
        }
    }

    [Fact]
    public async Task SaveQuotaStatusAsync_WithValidStatus_SavesSuccessfully()
    {
        // Arrange
        var quotaStatus = CreateTestQuotaStatusRecord();

        // Act
        await _stateManager.SaveQuotaStatusAsync(quotaStatus);

        // Assert
        var retrieved = await _stateManager.GetQuotaStatusAsync(quotaStatus.UserId);
        Assert.NotNull(retrieved);
        Assert.Equal(quotaStatus.UserId, retrieved.UserId);
        Assert.Equal(quotaStatus.TotalSpaceMB, retrieved.TotalSpaceMB);
        Assert.Equal(quotaStatus.UsedSpaceMB, retrieved.UsedSpaceMB);
        Assert.Equal(quotaStatus.HealthLevel, retrieved.HealthLevel);
        Assert.Equal(quotaStatus.CanAccommodateBackup, retrieved.CanAccommodateBackup);
    }

    [Fact]
    public async Task SaveQuotaStatusAsync_WithExistingStatus_UpdatesExisting()
    {
        // Arrange
        var originalStatus = CreateTestQuotaStatusRecord();
        await _stateManager.SaveQuotaStatusAsync(originalStatus);

        var updatedStatus = CreateTestQuotaStatusRecord();
        updatedStatus.UsedSpaceMB = 3000;
        updatedStatus.HealthLevel = "Warning";
        updatedStatus.CanAccommodateBackup = false;

        // Act
        await _stateManager.SaveQuotaStatusAsync(updatedStatus);

        // Assert
        var retrieved = await _stateManager.GetQuotaStatusAsync(updatedStatus.UserId);
        Assert.NotNull(retrieved);
        Assert.Equal(3000, retrieved.UsedSpaceMB);
        Assert.Equal("Warning", retrieved.HealthLevel);
        Assert.False(retrieved.CanAccommodateBackup);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_WithNonExistentUser_ReturnsNull()
    {
        // Act
        var result = await _stateManager.GetQuotaStatusAsync("non-existent-user");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveBackupRequirementsAsync_WithValidRequirements_SavesSuccessfully()
    {
        // Arrange
        var requirements = CreateTestBackupRequirementsRecord();

        // Act
        await _stateManager.SaveBackupRequirementsAsync(requirements);

        // Assert
        var retrieved = await _stateManager.GetBackupRequirementsAsync(requirements.UserId);
        Assert.NotNull(retrieved);
        Assert.Equal(requirements.UserId, retrieved.UserId);
        Assert.Equal(requirements.ProfileSizeMB, retrieved.ProfileSizeMB);
        Assert.Equal(requirements.EstimatedBackupSizeMB, retrieved.EstimatedBackupSizeMB);
        Assert.Equal(requirements.RequiredSpaceMB, retrieved.RequiredSpaceMB);
        Assert.Equal(requirements.CompressionFactor, retrieved.CompressionFactor);
    }

    [Fact]
    public async Task CreateQuotaWarningAsync_WithValidWarning_ReturnsWarningId()
    {
        // Arrange
        var warning = CreateTestQuotaWarning();

        // Act
        var warningId = await _stateManager.CreateQuotaWarningAsync(warning);

        // Assert
        Assert.True(warningId > 0);

        var retrieved = await _stateManager.GetQuotaWarningAsync(warningId);
        Assert.NotNull(retrieved);
        Assert.Equal(warning.UserId, retrieved.UserId);
        Assert.Equal(warning.WarningType, retrieved.WarningType);
        Assert.Equal(warning.Level, retrieved.Level);
        Assert.Equal(warning.Title, retrieved.Title);
        Assert.False(retrieved.IsResolved);
    }

    [Fact]
    public async Task GetUnresolvedQuotaWarningsAsync_WithMixedWarnings_ReturnsOnlyUnresolved()
    {
        // Arrange
        var userSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
        
        var unresolvedWarning = CreateTestQuotaWarning();
        var resolvedWarning = CreateTestQuotaWarning();
        resolvedWarning.Title = "Resolved Warning";

        var unresolvedId = await _stateManager.CreateQuotaWarningAsync(unresolvedWarning);
        var resolvedId = await _stateManager.CreateQuotaWarningAsync(resolvedWarning);

        // Resolve one warning
        await _stateManager.ResolveQuotaWarningAsync(resolvedId, "Test resolution");

        // Act
        var unresolvedWarnings = await _stateManager.GetUnresolvedQuotaWarningsAsync(userSid);

        // Assert
        Assert.Single(unresolvedWarnings);
        Assert.Equal(unresolvedId, unresolvedWarnings[0].Id);
        Assert.False(unresolvedWarnings[0].IsResolved);
    }

    [Fact]
    public async Task CreateQuotaEscalationAsync_WithValidEscalation_ReturnsEscalationId()
    {
        // Arrange
        var escalation = CreateTestQuotaEscalation();

        // Act
        var escalationId = await _stateManager.CreateQuotaEscalationAsync(escalation);

        // Assert
        Assert.True(escalationId > 0);

        var retrieved = await _stateManager.GetQuotaEscalationAsync(escalationId);
        Assert.NotNull(retrieved);
        Assert.Equal(escalation.UserId, retrieved.UserId);
        Assert.Equal(escalation.EscalationType, retrieved.EscalationType);
        Assert.Equal(escalation.Priority, retrieved.Priority);
        Assert.Equal(escalation.Title, retrieved.Title);
        Assert.Equal(EscalationStatus.Open, retrieved.Status);
    }

    [Fact]
    public async Task GetOpenQuotaEscalationsAsync_WithMixedEscalations_ReturnsOnlyOpen()
    {
        // Arrange
        var openEscalation = CreateTestQuotaEscalation();
        var resolvedEscalation = CreateTestQuotaEscalation();
        resolvedEscalation.Title = "Resolved Escalation";

        var openId = await _stateManager.CreateQuotaEscalationAsync(openEscalation);
        var resolvedId = await _stateManager.CreateQuotaEscalationAsync(resolvedEscalation);

        // Resolve one escalation
        await _stateManager.ResolveQuotaEscalationAsync(resolvedId, "Test resolution", "test-admin");

        // Act
        var openEscalations = await _stateManager.GetOpenQuotaEscalationsAsync();

        // Assert
        Assert.Single(openEscalations);
        Assert.Equal(openId, openEscalations[0].Id);
        Assert.Equal(EscalationStatus.Open, openEscalations[0].Status);
    }

    [Fact]
    public async Task GetQuotaHealthDistributionAsync_WithVariousHealthLevels_ReturnsCorrectDistribution()
    {
        // Arrange
        var statuses = new[]
        {
            CreateTestQuotaStatusRecord("user1", "Good"),
            CreateTestQuotaStatusRecord("user2", "Good"),
            CreateTestQuotaStatusRecord("user3", "Warning"),
            CreateTestQuotaStatusRecord("user4", "Critical")
        };

        foreach (var status in statuses)
        {
            await _stateManager.SaveQuotaStatusAsync(status);
        }

        // Act
        var distribution = await _stateManager.GetQuotaHealthDistributionAsync();

        // Assert
        Assert.Equal(3, distribution.Count);
        Assert.Equal(2, distribution[QuotaHealthLevel.Good]);
        Assert.Equal(1, distribution[QuotaHealthLevel.Warning]);
        Assert.Equal(1, distribution[QuotaHealthLevel.Critical]);
    }

    [Fact]
    public async Task GetTotalRequiredBackupSpaceAsync_WithMultipleUsers_ReturnsCorrectTotal()
    {
        // Arrange
        var requirements = new[]
        {
            CreateTestBackupRequirementsRecord("user1", 1000),
            CreateTestBackupRequirementsRecord("user2", 2000),
            CreateTestBackupRequirementsRecord("user3", 1500)
        };

        foreach (var req in requirements)
        {
            await _stateManager.SaveBackupRequirementsAsync(req);
        }

        // Act
        var total = await _stateManager.GetTotalRequiredBackupSpaceAsync();

        // Assert
        Assert.Equal(4500, total); // 1000 + 2000 + 1500
    }

    [Fact]
    public async Task GetActiveQuotaWarningCountAsync_WithMixedWarnings_ReturnsCorrectCount()
    {
        // Arrange
        var activeWarning1 = CreateTestQuotaWarning();
        var activeWarning2 = CreateTestQuotaWarning();
        var resolvedWarning = CreateTestQuotaWarning();

        await _stateManager.CreateQuotaWarningAsync(activeWarning1);
        await _stateManager.CreateQuotaWarningAsync(activeWarning2);
        var resolvedId = await _stateManager.CreateQuotaWarningAsync(resolvedWarning);

        // Resolve one warning
        await _stateManager.ResolveQuotaWarningAsync(resolvedId, "Test resolution");

        // Act
        var count = await _stateManager.GetActiveQuotaWarningCountAsync();

        // Assert
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetQuotaMetricsAsync_WithTestData_ReturnsComprehensiveMetrics()
    {
        // Arrange
        var startDate = DateTime.UtcNow.AddDays(-7);
        var endDate = DateTime.UtcNow;

        // Create test warnings
        var warning1 = CreateTestQuotaWarning();
        var warning2 = CreateTestQuotaWarning();
        warning2.WarningType = QuotaWarningType.LowSpace;
        warning2.Level = QuotaWarningLevel.Critical;

        await _stateManager.CreateQuotaWarningAsync(warning1);
        await _stateManager.CreateQuotaWarningAsync(warning2);

        // Create test escalations
        var escalation = CreateTestQuotaEscalation();
        await _stateManager.CreateQuotaEscalationAsync(escalation);

        // Create test quota statuses
        var status1 = CreateTestQuotaStatusRecord("user1", "Good");
        var status2 = CreateTestQuotaStatusRecord("user2", "Warning");
        await _stateManager.SaveQuotaStatusAsync(status1);
        await _stateManager.SaveQuotaStatusAsync(status2);

        // Act
        var metrics = await _stateManager.GetQuotaMetricsAsync(startDate, endDate);

        // Assert
        Assert.NotNull(metrics);
        Assert.Equal(startDate.Date, metrics.StartDate.Date);
        Assert.Equal(endDate.Date, metrics.EndDate.Date);
        Assert.True(metrics.TotalUsersAnalyzed > 0);
        Assert.True(metrics.WarningCountsByType.Count > 0);
        Assert.True(metrics.EscalationCountsByType.Count > 0);
    }

    private QuotaStatusRecord CreateTestQuotaStatusRecord(string? userId = null, string? healthLevel = null)
    {
        return new QuotaStatusRecord
        {
            UserId = userId ?? "S-1-5-21-1234567890-1234567890-1234567890-1001",
            TotalSpaceMB = 10000,
            UsedSpaceMB = 2000,
            AvailableSpaceMB = 8000,
            RequiredSpaceMB = 1000,
            HealthLevel = healthLevel ?? "Good",
            UsagePercentage = 20.0,
            CanAccommodateBackup = true,
            ShortfallMB = 0,
            Issues = null,
            Recommendations = null,
            LastChecked = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private BackupRequirementsRecord CreateTestBackupRequirementsRecord(string? userId = null, long? requiredMB = null)
    {
        return new BackupRequirementsRecord
        {
            UserId = userId ?? "S-1-5-21-1234567890-1234567890-1234567890-1001",
            ProfileSizeMB = 2000,
            EstimatedBackupSizeMB = 1400,
            CompressionFactor = 0.7,
            RequiredSpaceMB = requiredMB ?? 1000,
            FolderBreakdown = "{\"Desktop\":200,\"Documents\":800}",
            LastCalculated = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private QuotaWarning CreateTestQuotaWarning()
    {
        return new QuotaWarning
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            WarningType = QuotaWarningType.HighUsage,
            Level = QuotaWarningLevel.Warning,
            Title = "High OneDrive Usage",
            Message = "OneDrive usage is above the warning threshold",
            Details = "Current usage: 85%",
            Recommendations = "Consider cleaning up unnecessary files",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private QuotaEscalation CreateTestQuotaEscalation()
    {
        return new QuotaEscalation
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            EscalationType = QuotaEscalationType.InsufficientSpace,
            Priority = EscalationPriority.High,
            Status = EscalationStatus.Open,
            Title = "Insufficient OneDrive Space",
            Description = "User does not have enough OneDrive space for backup",
            IssueDetails = "Required: 5GB, Available: 2GB",
            RecommendedActions = "Increase user's OneDrive quota or clean up files",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}