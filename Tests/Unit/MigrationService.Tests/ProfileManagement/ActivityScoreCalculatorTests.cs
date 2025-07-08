using FluentAssertions;
using Microsoft.Extensions.Logging;
using MigrationService.Tests.TestUtilities;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;
using MigrationTool.Service.ProfileManagement.Native;
using Moq;
using Xunit;

namespace MigrationService.Tests.ProfileManagement;

public class ActivityScoreCalculatorTests
{
    private readonly Mock<ILogger<ActivityScoreCalculator>> _loggerMock;
    private readonly ActivityScoreCalculator _calculator;
    private readonly UserProfile _testProfile;
    private readonly ProfileMetrics _testMetrics;

    public ActivityScoreCalculatorTests()
    {
        _loggerMock = new Mock<ILogger<ActivityScoreCalculator>>();
        _calculator = new ActivityScoreCalculator(_loggerMock.Object);
        
        _testProfile = CreateTestProfile();
        _testMetrics = CreateTestMetrics();
    }

    [Fact]
    public async Task CalculateScoreAsync_ReturnsValidResult()
    {
        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.Should().NotBeNull();
        result.UserProfile.Should().BeSameAs(_testProfile);
        result.CalculationTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.TotalScore.Should().BeInRange(0, 100);
    }

    [Fact]
    public async Task CalculateScoreAsync_CalculatesAllComponentScores()
    {
        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores.Should().ContainKey(ActivityComponent.LoginRecency);
        result.ComponentScores.Should().ContainKey(ActivityComponent.FileActivity);
        result.ComponentScores.Should().ContainKey(ActivityComponent.ActiveProcesses);
        result.ComponentScores.Should().ContainKey(ActivityComponent.ProfileSize);
        result.ComponentScores.Should().ContainKey(ActivityComponent.SessionActivity);
        result.ComponentScores.Should().HaveCount(5);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithRecentLogin_GivesHighLoginScore()
    {
        // Arrange
        _testMetrics.LastLoginTime = DateTime.UtcNow.AddHours(-12);

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.LoginRecency].RawScore.Should().BeGreaterOrEqualTo(80);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithOldLogin_GivesLowLoginScore()
    {
        // Arrange
        _testMetrics.LastLoginTime = DateTime.UtcNow.AddDays(-120);

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.LoginRecency].RawScore.Should().BeLessOrEqualTo(20);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithActiveProcesses_GivesHighProcessScore()
    {
        // Arrange
        _testMetrics.ActiveProcessCount = 15;

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.ActiveProcesses].RawScore.Should().BeGreaterOrEqualTo(60);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithLargeProfile_GivesHighSizeScore()
    {
        // Arrange
        _testMetrics.ProfileSizeBytes = 10L * 1024 * 1024 * 1024; // 10GB

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.ProfileSize].RawScore.Should().Be(100);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithActiveSession_GivesHighSessionScore()
    {
        // Arrange
        _testMetrics.HasActiveSession = true;

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.SessionActivity].RawScore.Should().Be(100);
    }

    [Fact]
    public async Task CalculateScoreAsync_WithEnhancedData_UsesIt()
    {
        // Arrange
        var activityData = UserActivityDataMockHelper.CreateMockActivityData(_testProfile.UserId);
        var processInfo = UserProcessInfoMockHelper.CreateMockProcessInfo(_testProfile.UserId);
        var fileActivity = FileActivityReportMockHelper.CreateMockReport(_testProfile.ProfilePath);

        // Act
        var result = await _calculator.CalculateScoreAsync(
            _testProfile, _testMetrics, activityData, processInfo, fileActivity);

        // Assert
        result.TotalScore.Should().BeGreaterThan(0);
        result.Confidence.Should().Match(c => c == ActivityConfidence.High || c == ActivityConfidence.Medium);
    }

    [Fact]
    public async Task CalculateScoreAsync_AppliesWeightsCorrectly()
    {
        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        foreach (var component in result.ComponentScores.Values)
        {
            component.WeightedScore.Should().Be(component.RawScore * component.Weight / 100.0);
        }
    }

    [Fact]
    public async Task CalculateScoreAsync_ClassifiesActivityLevel()
    {
        // Arrange - Very active profile
        _testMetrics.LastLoginTime = DateTime.UtcNow.AddHours(-1);
        _testMetrics.ActiveProcessCount = 20;
        _testMetrics.HasActiveSession = true;
        _testMetrics.ProfileSizeBytes = 5L * 1024 * 1024 * 1024;

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ActivityLevel.Should().Match(l => l == UserActivityLevel.Active || l == UserActivityLevel.VeryActive);
    }

    [Fact]
    public async Task CalculateScoreAsync_GeneratesRecommendations()
    {
        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.Recommendations.Should().NotBeNull();
        result.Recommendations.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CalculateScoreAsync_WithLowConfidence_AddsRecommendation()
    {
        // Arrange - Minimal data
        var minimalMetrics = new ProfileMetrics
        {
            ProfileSizeBytes = 10 * 1024, // 10KB
            LastLoginTime = DateTime.MinValue,
            LastActivityTime = DateTime.MinValue
        };

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, minimalMetrics);

        // Assert
        result.Confidence.Should().Match(c => c == ActivityConfidence.VeryLow || c == ActivityConfidence.Low);
        result.Recommendations.Should().Contain(r => r.Contains("confidence"));
    }

    [Fact]
    public async Task CalculateScoreAsync_HandlesErrors_Gracefully()
    {
        // Arrange
        UserProfile? nullProfile = null;

        // Act & Assert
        await _calculator.Invoking(c => c.CalculateScoreAsync(nullProfile!, _testMetrics))
            .Should().ThrowAsync<NullReferenceException>();
    }

    [Theory]
    [InlineData(90, UserActivityLevel.VeryActive)]
    [InlineData(70, UserActivityLevel.Active)]
    [InlineData(50, UserActivityLevel.Moderate)]
    [InlineData(30, UserActivityLevel.Low)]
    [InlineData(10, UserActivityLevel.Inactive)]
    public async Task ActivityLevel_MapsCorrectlyFromScore(int targetScore, UserActivityLevel expectedLevel)
    {
        // Arrange - Set metrics to achieve target score
        AdjustMetricsForTargetScore(_testMetrics, targetScore);

        // Act
        var result = await _calculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ActivityLevel.Should().Be(expectedLevel);
    }

    [Fact]
    public async Task CustomConfiguration_AppliesCorrectWeights()
    {
        // Arrange
        var customConfig = new ActivityScoringConfiguration
        {
            Weights = new Dictionary<ActivityComponent, int>
            {
                { ActivityComponent.LoginRecency, 60 },
                { ActivityComponent.FileActivity, 20 },
                { ActivityComponent.ActiveProcesses, 10 },
                { ActivityComponent.ProfileSize, 5 },
                { ActivityComponent.SessionActivity, 5 }
            }
        };
        
        var customCalculator = new ActivityScoreCalculator(_loggerMock.Object, customConfig);

        // Act
        var result = await customCalculator.CalculateScoreAsync(_testProfile, _testMetrics);

        // Assert
        result.ComponentScores[ActivityComponent.LoginRecency].Weight.Should().Be(60);
        result.ComponentScores[ActivityComponent.FileActivity].Weight.Should().Be(20);
    }

    private UserProfile CreateTestProfile()
    {
        return new UserProfile
        {
            UserId = "S-1-5-21-1234567890-1234567890-1234567890-1001",
            UserName = "TestUser",
            ProfilePath = @"C:\Users\TestUser",
            ProfileType = ProfileType.Local,
            LastLoginTime = DateTime.UtcNow.AddDays(-7),
            IsActive = true,
            ProfileSizeBytes = 1024L * 1024 * 1024, // 1GB
            RequiresBackup = true,
            BackupPriority = 50,
            CreatedAt = DateTime.UtcNow.AddDays(-365),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        };
    }

    private ProfileMetrics CreateTestMetrics()
    {
        return new ProfileMetrics
        {
            ProfileSizeBytes = 1024L * 1024 * 1024, // 1GB
            LastLoginTime = DateTime.UtcNow.AddDays(-7),
            LastActivityTime = DateTime.UtcNow.AddDays(-3),
            ActiveProcessCount = 5,
            HasRecentActivity = true,
            IsAccessible = true,
            IsLoaded = false,
            HasActiveSession = false,
            Classification = ProfileClassification.Active
        };
    }

    private void AdjustMetricsForTargetScore(ProfileMetrics metrics, int targetScore)
    {
        if (targetScore >= 80)
        {
            metrics.LastLoginTime = DateTime.UtcNow.AddHours(-6);
            metrics.ActiveProcessCount = 15;
            metrics.HasActiveSession = true;
            metrics.ProfileSizeBytes = 5L * 1024 * 1024 * 1024;
        }
        else if (targetScore >= 60)
        {
            metrics.LastLoginTime = DateTime.UtcNow.AddDays(-5);
            metrics.ActiveProcessCount = 8;
            metrics.HasActiveSession = false;
            metrics.ProfileSizeBytes = 2L * 1024 * 1024 * 1024;
        }
        else if (targetScore >= 40)
        {
            metrics.LastLoginTime = DateTime.UtcNow.AddDays(-20);
            metrics.ActiveProcessCount = 3;
            metrics.HasActiveSession = false;
            metrics.ProfileSizeBytes = 500L * 1024 * 1024;
        }
        else if (targetScore >= 20)
        {
            metrics.LastLoginTime = DateTime.UtcNow.AddDays(-60);
            metrics.ActiveProcessCount = 1;
            metrics.HasActiveSession = false;
            metrics.ProfileSizeBytes = 200L * 1024 * 1024;
        }
        else
        {
            metrics.LastLoginTime = DateTime.UtcNow.AddDays(-180);
            metrics.ActiveProcessCount = 0;
            metrics.HasActiveSession = false;
            metrics.ProfileSizeBytes = 50L * 1024 * 1024;
        }
    }
}