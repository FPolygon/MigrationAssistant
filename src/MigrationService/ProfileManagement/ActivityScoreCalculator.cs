using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Calculates comprehensive activity scores for user profiles using weighted algorithms
/// </summary>
[SupportedOSPlatform("windows")]
public class ActivityScoreCalculator
{
    private readonly ILogger<ActivityScoreCalculator> _logger;
    private readonly ActivityScoringConfiguration _configuration;

    public ActivityScoreCalculator(
        ILogger<ActivityScoreCalculator> logger,
        ActivityScoringConfiguration? configuration = null)
    {
        _logger = logger;
        _configuration = configuration ?? ActivityScoringConfiguration.Default;
    }

    /// <summary>
    /// Calculates a comprehensive activity score for a user profile
    /// </summary>
    public Task<ActivityScoreResult> CalculateScoreAsync(
        UserProfile profile,
        ProfileMetrics metrics,
        UserActivityData? activityData = null,
        UserProcessInfo? processInfo = null,
        FileActivityReport? fileActivity = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calculating activity score for user: {UserName} ({UserId})", 
            profile.UserName, profile.UserId);

        var result = new ActivityScoreResult
        {
            UserProfile = profile,
            CalculationTime = DateTime.UtcNow
        };

        try
        {
            // Calculate component scores
            var loginScore = CalculateLoginScore(metrics, activityData);
            var fileScore = CalculateFileActivityScore(fileActivity, metrics);
            var processScore = CalculateProcessScore(processInfo, metrics);
            var profileScore = CalculateProfileScore(metrics);
            var sessionScore = CalculateSessionScore(activityData, metrics);

            // Apply weights
            result.ComponentScores[ActivityComponent.LoginRecency] = loginScore;
            result.ComponentScores[ActivityComponent.FileActivity] = fileScore;
            result.ComponentScores[ActivityComponent.ActiveProcesses] = processScore;
            result.ComponentScores[ActivityComponent.ProfileSize] = profileScore;
            result.ComponentScores[ActivityComponent.SessionActivity] = sessionScore;

            // Calculate weighted total
            result.TotalScore = CalculateWeightedScore(result.ComponentScores);

            // Determine confidence level
            result.Confidence = DetermineConfidence(result);

            // Classify activity level
            result.ActivityLevel = ClassifyActivityLevel(result.TotalScore);

            // Generate recommendations
            result.Recommendations = GenerateRecommendations(result, metrics);

            _logger.LogInformation(
                "Activity score for {UserName}: {Score}/100 ({Level}), Confidence: {Confidence}",
                profile.UserName, result.TotalScore, result.ActivityLevel, result.Confidence);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate activity score for user: {UserName}", profile.UserName);
            result.Errors.Add($"Score calculation failed: {ex.Message}");
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// Calculates login recency score
    /// </summary>
    private ComponentScore CalculateLoginScore(ProfileMetrics metrics, UserActivityData? activityData)
    {
        var score = new ComponentScore
        {
            Component = ActivityComponent.LoginRecency,
            Weight = _configuration.Weights[ActivityComponent.LoginRecency]
        };

        // Use enhanced data if available
        var lastLogin = activityData?.LastInteractiveLogon ?? metrics.LastLoginTime;
        var daysSinceLogin = (DateTime.UtcNow - lastLogin).TotalDays;

        // Score based on recency
        if (daysSinceLogin < 1)
        {
            score.RawScore = 100;
            score.Details["Category"] = "Very Recent";
        }
        else if (daysSinceLogin < 7)
        {
            score.RawScore = 80;
            score.Details["Category"] = "Recent";
        }
        else if (daysSinceLogin < 30)
        {
            score.RawScore = 60;
            score.Details["Category"] = "Moderate";
        }
        else if (daysSinceLogin < 90)
        {
            score.RawScore = 30;
            score.Details["Category"] = "Old";
        }
        else
        {
            score.RawScore = 10;
            score.Details["Category"] = "Very Old";
        }

        // Bonus for multiple recent logons
        if (activityData != null)
        {
            var recentLogons = activityData.LogonEvents
                .Where(e => e.EventType == LogonEventType.Logon && 
                           e.EventTime > DateTime.UtcNow.AddDays(-7))
                .Count();

            if (recentLogons > 10)
                score.RawScore = Math.Min(score.RawScore + 10, 100);

            score.Details["RecentLogons"] = recentLogons.ToString();
        }

        score.Details["DaysSinceLogin"] = daysSinceLogin.ToString("F1");
        score.Details["LastLogin"] = lastLogin.ToString("yyyy-MM-dd HH:mm:ss");
        score.WeightedScore = score.RawScore * score.Weight / 100.0;

        return score;
    }

    /// <summary>
    /// Calculates file activity score
    /// </summary>
    private ComponentScore CalculateFileActivityScore(FileActivityReport? fileActivity, ProfileMetrics metrics)
    {
        var score = new ComponentScore
        {
            Component = ActivityComponent.FileActivity,
            Weight = _configuration.Weights[ActivityComponent.FileActivity]
        };

        if (fileActivity != null)
        {
            // Use detailed file activity data
            score.RawScore = fileActivity.ActivityScore;
            score.Details["RecentFiles"] = fileActivity.RecentFileCount.ToString();
            score.Details["VeryRecentFiles"] = fileActivity.VeryRecentFileCount.ToString();
            score.Details["ActivityLevel"] = fileActivity.ActivityLevel.ToString();
        }
        else
        {
            // Fallback to basic metrics
            var daysSinceActivity = (DateTime.UtcNow - metrics.LastActivityTime).TotalDays;
            
            if (daysSinceActivity < 1)
                score.RawScore = 90;
            else if (daysSinceActivity < 7)
                score.RawScore = 70;
            else if (daysSinceActivity < 30)
                score.RawScore = 40;
            else
                score.RawScore = 10;

            score.Details["DaysSinceActivity"] = daysSinceActivity.ToString("F1");
        }

        score.WeightedScore = score.RawScore * score.Weight / 100.0;
        return score;
    }

    /// <summary>
    /// Calculates process activity score
    /// </summary>
    private ComponentScore CalculateProcessScore(UserProcessInfo? processInfo, ProfileMetrics metrics)
    {
        var score = new ComponentScore
        {
            Component = ActivityComponent.ActiveProcesses,
            Weight = _configuration.Weights[ActivityComponent.ActiveProcesses]
        };

        if (processInfo != null)
        {
            // Score based on process count and types
            var baseScore = 0;

            // Interactive processes are most important
            if (processInfo.InteractiveProcessCount > 5)
                baseScore = 90;
            else if (processInfo.InteractiveProcessCount > 2)
                baseScore = 70;
            else if (processInfo.InteractiveProcessCount > 0)
                baseScore = 50;
            else if (processInfo.BackgroundProcessCount > 0)
                baseScore = 30;

            // Bonus for key processes
            if (processInfo.HasExplorerProcess)
                baseScore += 10;
            if (processInfo.HasBrowserProcess)
                baseScore += 5;
            if (processInfo.HasProductivityProcess)
                baseScore += 5;

            score.RawScore = Math.Min(baseScore, 100);
            score.Details["TotalProcesses"] = processInfo.TotalProcessCount.ToString();
            score.Details["InteractiveProcesses"] = processInfo.InteractiveProcessCount.ToString();
            score.Details["HasExplorer"] = processInfo.HasExplorerProcess.ToString();
        }
        else
        {
            // Fallback to basic metrics
            if (metrics.ActiveProcessCount > 10)
                score.RawScore = 80;
            else if (metrics.ActiveProcessCount > 5)
                score.RawScore = 60;
            else if (metrics.ActiveProcessCount > 0)
                score.RawScore = 40;
            else
                score.RawScore = 0;

            score.Details["ProcessCount"] = metrics.ActiveProcessCount.ToString();
        }

        score.WeightedScore = score.RawScore * score.Weight / 100.0;
        return score;
    }

    /// <summary>
    /// Calculates profile size and growth score
    /// </summary>
    private ComponentScore CalculateProfileScore(ProfileMetrics metrics)
    {
        var score = new ComponentScore
        {
            Component = ActivityComponent.ProfileSize,
            Weight = _configuration.Weights[ActivityComponent.ProfileSize]
        };

        // Score based on profile size
        var sizeMB = metrics.ProfileSizeMB;
        
        if (sizeMB > 5000) // >5GB
            score.RawScore = 100;
        else if (sizeMB > 1000) // >1GB
            score.RawScore = 80;
        else if (sizeMB > 500) // >500MB
            score.RawScore = 60;
        else if (sizeMB > 100) // >100MB
            score.RawScore = 40;
        else if (sizeMB > 50) // >50MB
            score.RawScore = 20;
        else
            score.RawScore = 10;

        score.Details["SizeMB"] = sizeMB.ToString();
        score.Details["SizeCategory"] = GetSizeCategory(sizeMB);
        score.WeightedScore = score.RawScore * score.Weight / 100.0;

        return score;
    }

    /// <summary>
    /// Calculates session activity score
    /// </summary>
    private ComponentScore CalculateSessionScore(UserActivityData? activityData, ProfileMetrics metrics)
    {
        var score = new ComponentScore
        {
            Component = ActivityComponent.SessionActivity,
            Weight = _configuration.Weights[ActivityComponent.SessionActivity]
        };

        // Check for active session
        if (activityData?.HasActiveSession == true || metrics.HasActiveSession)
        {
            score.RawScore = 100;
            score.Details["HasActiveSession"] = "true";
        }
        else if (metrics.IsLoaded || activityData?.IsRegistryLoaded == true)
        {
            score.RawScore = 70;
            score.Details["ProfileLoaded"] = "true";
        }
        else if (activityData?.HasRdpActivity == true)
        {
            score.RawScore = 60;
            score.Details["HasRdpActivity"] = "true";
        }
        else
        {
            score.RawScore = 0;
            score.Details["NoActiveSession"] = "true";
        }

        // Add recent unlock bonus
        if (activityData != null && (DateTime.UtcNow - activityData.LastUnlock).TotalHours < 24)
        {
            score.RawScore = Math.Min(score.RawScore + 20, 100);
            score.Details["RecentUnlock"] = "true";
        }

        score.WeightedScore = score.RawScore * score.Weight / 100.0;
        return score;
    }

    /// <summary>
    /// Calculates weighted total score
    /// </summary>
    private int CalculateWeightedScore(Dictionary<ActivityComponent, ComponentScore> componentScores)
    {
        var totalWeightedScore = componentScores.Values.Sum(s => s.WeightedScore);
        var totalWeight = componentScores.Values.Sum(s => s.Weight);
        
        if (totalWeight == 0)
            return 0;

        return (int)Math.Round(totalWeightedScore * 100 / totalWeight);
    }

    /// <summary>
    /// Determines confidence level of the score
    /// </summary>
    private ActivityConfidence DetermineConfidence(ActivityScoreResult result)
    {
        var dataPoints = 0;
        var totalPossiblePoints = 5;

        // Check which data sources we have
        if (result.ComponentScores[ActivityComponent.LoginRecency].RawScore > 0)
            dataPoints++;
        if (result.ComponentScores[ActivityComponent.FileActivity].Details.ContainsKey("ActivityLevel"))
            dataPoints++;
        if (result.ComponentScores[ActivityComponent.ActiveProcesses].Details.ContainsKey("InteractiveProcesses"))
            dataPoints++;
        if (result.ComponentScores[ActivityComponent.SessionActivity].RawScore > 0)
            dataPoints++;
        if (result.ComponentScores[ActivityComponent.ProfileSize].RawScore > 20)
            dataPoints++;

        var confidenceRatio = (float)dataPoints / totalPossiblePoints;

        if (confidenceRatio >= 0.8)
            return ActivityConfidence.High;
        else if (confidenceRatio >= 0.6)
            return ActivityConfidence.Medium;
        else if (confidenceRatio >= 0.4)
            return ActivityConfidence.Low;
        else
            return ActivityConfidence.VeryLow;
    }

    /// <summary>
    /// Classifies activity level based on score
    /// </summary>
    private UserActivityLevel ClassifyActivityLevel(int score)
    {
        if (score >= 80)
            return UserActivityLevel.VeryActive;
        else if (score >= 60)
            return UserActivityLevel.Active;
        else if (score >= 40)
            return UserActivityLevel.Moderate;
        else if (score >= 20)
            return UserActivityLevel.Low;
        else
            return UserActivityLevel.Inactive;
    }

    /// <summary>
    /// Generates recommendations based on activity analysis
    /// </summary>
    private List<string> GenerateRecommendations(ActivityScoreResult result, ProfileMetrics metrics)
    {
        var recommendations = new List<string>();

        // Check activity level
        if (result.ActivityLevel >= UserActivityLevel.Active)
        {
            recommendations.Add("User is actively using this system - backup is strongly recommended");
        }
        else if (result.ActivityLevel == UserActivityLevel.Moderate)
        {
            recommendations.Add("User shows moderate activity - backup is recommended");
        }
        else if (result.ActivityLevel == UserActivityLevel.Low)
        {
            recommendations.Add("User shows low activity - consider if backup is necessary");
        }
        else
        {
            recommendations.Add("User appears inactive - backup may not be necessary");
        }

        // Check confidence
        if (result.Confidence <= ActivityConfidence.Low)
        {
            recommendations.Add("Low confidence score - consider manual verification");
        }

        // Check specific issues
        if (result.ComponentScores[ActivityComponent.ProfileSize].RawScore < 20)
        {
            recommendations.Add("Very small profile size - may be a temporary or new profile");
        }

        if (result.ComponentScores[ActivityComponent.LoginRecency].RawScore < 30)
        {
            recommendations.Add("No recent login activity detected");
        }

        if (!metrics.IsAccessible)
        {
            recommendations.Add("Profile directory is not accessible - may need administrator privileges");
        }

        return recommendations;
    }

    /// <summary>
    /// Gets size category description
    /// </summary>
    private string GetSizeCategory(long sizeMB)
    {
        if (sizeMB > 10000) return "Very Large (>10GB)";
        if (sizeMB > 5000) return "Large (5-10GB)";
        if (sizeMB > 1000) return "Medium (1-5GB)";
        if (sizeMB > 500) return "Small (500MB-1GB)";
        if (sizeMB > 100) return "Very Small (100-500MB)";
        return "Minimal (<100MB)";
    }
}

/// <summary>
/// Configuration for activity scoring
/// </summary>
public class ActivityScoringConfiguration
{
    public Dictionary<ActivityComponent, int> Weights { get; set; } = new();
    
    public static ActivityScoringConfiguration Default => new()
    {
        Weights = new Dictionary<ActivityComponent, int>
        {
            { ActivityComponent.LoginRecency, 40 },
            { ActivityComponent.FileActivity, 25 },
            { ActivityComponent.ActiveProcesses, 20 },
            { ActivityComponent.ProfileSize, 10 },
            { ActivityComponent.SessionActivity, 5 }
        }
    };

    public static ActivityScoringConfiguration Conservative => new()
    {
        Weights = new Dictionary<ActivityComponent, int>
        {
            { ActivityComponent.LoginRecency, 50 },
            { ActivityComponent.FileActivity, 20 },
            { ActivityComponent.ActiveProcesses, 15 },
            { ActivityComponent.ProfileSize, 10 },
            { ActivityComponent.SessionActivity, 5 }
        }
    };

    public static ActivityScoringConfiguration Aggressive => new()
    {
        Weights = new Dictionary<ActivityComponent, int>
        {
            { ActivityComponent.LoginRecency, 30 },
            { ActivityComponent.FileActivity, 30 },
            { ActivityComponent.ActiveProcesses, 25 },
            { ActivityComponent.ProfileSize, 10 },
            { ActivityComponent.SessionActivity, 5 }
        }
    };
}

/// <summary>
/// Result of activity score calculation
/// </summary>
public class ActivityScoreResult
{
    public UserProfile UserProfile { get; set; } = null!;
    public DateTime CalculationTime { get; set; }
    
    // Overall score
    public int TotalScore { get; set; }
    public UserActivityLevel ActivityLevel { get; set; }
    public ActivityConfidence Confidence { get; set; }
    
    // Component scores
    public Dictionary<ActivityComponent, ComponentScore> ComponentScores { get; set; } = new();
    
    // Analysis results
    public List<string> Recommendations { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Score for an individual activity component
/// </summary>
public class ComponentScore
{
    public ActivityComponent Component { get; set; }
    public int RawScore { get; set; }
    public int Weight { get; set; }
    public double WeightedScore { get; set; }
    public Dictionary<string, string> Details { get; set; } = new();
}

/// <summary>
/// Activity components used in scoring
/// </summary>
public enum ActivityComponent
{
    LoginRecency,
    FileActivity,
    ActiveProcesses,
    ProfileSize,
    SessionActivity
}

/// <summary>
/// User activity levels
/// </summary>
public enum UserActivityLevel
{
    Inactive,
    Low,
    Moderate,
    Active,
    VeryActive
}

/// <summary>
/// Confidence levels for activity scores
/// </summary>
public enum ActivityConfidence
{
    VeryLow,
    Low,
    Medium,
    High
}