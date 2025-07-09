using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Classifies user profiles based on activity, size, and other criteria
/// </summary>
[SupportedOSPlatform("windows")]
public class ProfileClassifier : IProfileClassifier
{
    private readonly ILogger<ProfileClassifier> _logger;
    private readonly IProfileActivityAnalyzer _activityAnalyzer;
    private readonly IActivityScoreCalculator? _scoreCalculator;
    private readonly ClassificationRuleEngine? _ruleEngine;
    private readonly IClassificationOverrideManager? _overrideManager;
    private readonly IStateManager? _stateManager;

    // Configuration for classification rules
    private readonly ProfileClassificationConfig _config;

    public ProfileClassifier(
        ILogger<ProfileClassifier> logger,
        IProfileActivityAnalyzer activityAnalyzer,
        IActivityScoreCalculator? scoreCalculator = null,
        ClassificationRuleEngine? ruleEngine = null,
        IClassificationOverrideManager? overrideManager = null,
        IStateManager? stateManager = null,
        ProfileClassificationConfig? config = null)
    {
        _logger = logger;
        _activityAnalyzer = activityAnalyzer;
        _scoreCalculator = scoreCalculator;
        _ruleEngine = ruleEngine;
        _overrideManager = overrideManager;
        _stateManager = stateManager;
        _config = config ?? ProfileClassificationConfig.Default;
    }

    /// <summary>
    /// Classifies a user profile based on its metrics and characteristics
    /// </summary>
    public async Task<ProfileClassificationResult> ClassifyProfileAsync(
        UserProfile profile,
        ProfileMetrics metrics,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Classifying profile: {UserName} ({UserId})", profile.UserName, profile.UserId);

        var result = new ProfileClassificationResult
        {
            UserId = profile.UserId,
            UserName = profile.UserName,
            Metrics = metrics
        };

        try
        {
            // Check for manual override first
            if (_overrideManager != null)
            {
                var overrideCheck = await _overrideManager.CheckOverrideAsync(
                    profile.UserId, ProfileClassification.Unknown, cancellationToken);

                if (overrideCheck.HasOverride)
                {
                    result.Classification = overrideCheck.EffectiveClassification;
                    result.IsOverridden = true;
                    result.Reason = $"Manual override: {overrideCheck.OverrideReason}";
                    DetermineBackupRequirements(result, profile, metrics);

                    _logger.LogInformation(
                        "Profile {UserName} classification overridden to {Classification}",
                        profile.UserName, result.Classification);

                    await SaveClassificationAsync(result, cancellationToken);
                    return result;
                }
            }

            // Check for system accounts first
            if (IsSystemAccount(profile))
            {
                result.Classification = ProfileClassification.System;
                result.RequiresBackup = false;
                result.BackupPriority = 0;
                result.Reason = "System or service account";
                await SaveClassificationAsync(result, cancellationToken);
                return result;
            }

            // Check for temporary profiles
            if (IsTemporaryProfile(profile))
            {
                result.Classification = ProfileClassification.Temporary;
                result.RequiresBackup = false;
                result.BackupPriority = 0;
                result.Reason = "Temporary profile";
                await SaveClassificationAsync(result, cancellationToken);
                return result;
            }

            // Check for corrupted profiles
            if (!metrics.IsAccessible || metrics.Classification == ProfileClassification.Corrupted)
            {
                result.Classification = ProfileClassification.Corrupted;
                result.RequiresBackup = false;
                result.BackupPriority = 0;
                result.Reason = "Profile corrupted or inaccessible";
                result.Errors.AddRange(metrics.Errors);
                await SaveClassificationAsync(result, cancellationToken);
                return result;
            }

            // Try rule engine if available
            if (_ruleEngine != null && _scoreCalculator != null)
            {
                var scoreResult = await _scoreCalculator.CalculateScoreAsync(profile, metrics, cancellationToken: cancellationToken);
                var ruleResult = await _ruleEngine.EvaluateRulesAsync(profile, metrics, scoreResult, _config.RuleSetName, cancellationToken);

                if (ruleResult.Classification != ProfileClassification.Unknown)
                {
                    result.Classification = ruleResult.Classification;
                    result.Confidence = ruleResult.Confidence;
                    result.Reason = ruleResult.ClassificationReason;
                    result.RuleSetName = ruleResult.RuleSetName;
                    result.ActivityScore = scoreResult.TotalScore;

                    DetermineBackupRequirements(result, profile, metrics);
                    await SaveClassificationAsync(result, cancellationToken);
                    return result;
                }
            }

            // Fall back to activity-based classification
            var classification = await ClassifyByActivityAsync(profile, metrics, cancellationToken);
            result.Classification = classification;

            // Determine backup requirements
            DetermineBackupRequirements(result, profile, metrics);

            // Save classification
            await SaveClassificationAsync(result, cancellationToken);

            _logger.LogInformation(
                "Profile classified: {UserName} as {Classification}, RequiresBackup={RequiresBackup}, Priority={Priority}",
                profile.UserName, result.Classification, result.RequiresBackup, result.BackupPriority);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to classify profile: {UserName}", profile.UserName);
            result.Classification = ProfileClassification.Unknown;
            result.Errors.Add($"Classification failed: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Determines if a profile belongs to a system account
    /// </summary>
    private bool IsSystemAccount(UserProfile profile)
    {
        // Well-known system SIDs
        var systemSids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "S-1-5-18", // LOCAL_SYSTEM
            "S-1-5-19", // LOCAL_SERVICE
            "S-1-5-20", // NETWORK_SERVICE
        };

        if (systemSids.Contains(profile.UserId))
        {
            return true;
        }

        // Check for service account patterns
        if (profile.UserId.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase) || // Service accounts
            profile.UserId.StartsWith("S-1-5-82-", StringComparison.OrdinalIgnoreCase) || // IIS AppPool
            profile.UserId.StartsWith("S-1-5-83-", StringComparison.OrdinalIgnoreCase) || // Virtual accounts
            profile.UserId.StartsWith("S-1-5-90-", StringComparison.OrdinalIgnoreCase) || // Windows Manager
            profile.UserId.StartsWith("S-1-5-96-", StringComparison.OrdinalIgnoreCase))   // Font Driver Host
        {
            return true;
        }

        // Check username patterns
        var systemUserPatterns = new[] { "SYSTEM", "SERVICE", "NETWORK", "$", "IUSR", "ASPNET" };
        return systemUserPatterns.Any(pattern =>
            profile.UserName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determines if a profile is temporary
    /// </summary>
    private bool IsTemporaryProfile(UserProfile profile)
    {
        return profile.ProfilePath.EndsWith(".TEMP", StringComparison.OrdinalIgnoreCase) ||
               profile.ProfilePath.EndsWith(".TMP", StringComparison.OrdinalIgnoreCase) ||
               profile.ProfilePath.Contains("TEMP", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Classifies profile based on activity metrics
    /// </summary>
    private async Task<ProfileClassification> ClassifyByActivityAsync(
        UserProfile profile,
        ProfileMetrics metrics,
        CancellationToken cancellationToken)
    {
        // Use enhanced scoring if available
        if (_scoreCalculator != null)
        {
            try
            {
                var scoreResult = await _scoreCalculator.CalculateScoreAsync(profile, metrics, cancellationToken: cancellationToken);

                // Map activity level to classification
                return scoreResult.ActivityLevel switch
                {
                    UserActivityLevel.VeryActive or UserActivityLevel.Active => ProfileClassification.Active,
                    UserActivityLevel.Moderate =>
                        metrics.ProfileSizeMB >= _config.MinimumActiveSizeMB ? ProfileClassification.Active : ProfileClassification.Inactive,
                    UserActivityLevel.Low or UserActivityLevel.Inactive => ProfileClassification.Inactive,
                    _ => ProfileClassification.Unknown
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to use score calculator, falling back to basic classification");
                // Fall through to basic classification
            }
        }

        // Basic classification (fallback)
        return await Task.Run(() =>
        {
            // Check if profile is currently active (loaded with processes)
            if (metrics.IsLoaded && metrics.ActiveProcessCount > 0)
            {
                return ProfileClassification.Active;
            }

            // Check activity age
            var activityAge = DateTime.UtcNow - metrics.LastActivityTime;
            var loginAge = DateTime.UtcNow - metrics.LastLoginTime;

            // Active: Recent activity AND sufficient size
            if (activityAge <= _config.ActiveThreshold &&
                metrics.ProfileSizeMB >= _config.MinimumActiveSizeMB)
            {
                return ProfileClassification.Active;
            }

            // Active: Recent login even if no recent file activity (user might be on vacation)
            if (loginAge <= _config.ActiveThreshold &&
                metrics.ProfileSizeMB >= _config.MinimumActiveSizeMB)
            {
                return ProfileClassification.Active;
            }

            // Inactive: Old activity OR small profile
            if (activityAge > _config.InactiveThreshold ||
                metrics.ProfileSizeMB < _config.MinimumActiveSizeMB)
            {
                return ProfileClassification.Inactive;
            }

            // Default to active if unsure (better to backup than to miss data)
            return ProfileClassification.Active;
        }, cancellationToken);
    }

    /// <summary>
    /// Determines backup requirements based on classification
    /// </summary>
    private void DetermineBackupRequirements(
        ProfileClassificationResult result,
        UserProfile profile,
        ProfileMetrics metrics)
    {
        switch (result.Classification)
        {
            case ProfileClassification.Active:
                result.RequiresBackup = true;
                result.BackupPriority = CalculateBackupPriority(profile, metrics, isActive: true);
                result.Reason = $"Active user (last activity: {metrics.LastActivityTime:yyyy-MM-dd}, size: {metrics.ProfileSizeMB}MB)";
                break;

            case ProfileClassification.Inactive:
                // Inactive profiles may still require backup based on policy
                result.RequiresBackup = _config.BackupInactiveProfiles && metrics.ProfileSizeMB >= _config.MinimumBackupSizeMB;
                result.BackupPriority = result.RequiresBackup ? CalculateBackupPriority(profile, metrics, isActive: false) : 0;
                result.Reason = $"Inactive user (last activity: {metrics.LastActivityTime:yyyy-MM-dd}, size: {metrics.ProfileSizeMB}MB)";
                break;

            case ProfileClassification.System:
            case ProfileClassification.Temporary:
            case ProfileClassification.Corrupted:
                result.RequiresBackup = false;
                result.BackupPriority = 0;
                // Reason already set
                break;

            default:
                result.RequiresBackup = false;
                result.BackupPriority = 0;
                result.Reason = "Unknown classification";
                break;
        }
    }

    /// <summary>
    /// Calculates backup priority based on various factors
    /// </summary>
    private int CalculateBackupPriority(UserProfile profile, ProfileMetrics metrics, bool isActive)
    {
        var priority = isActive ? 100 : 50; // Base priority

        // Adjust based on profile size (larger profiles = higher priority)
        if (metrics.ProfileSizeMB > 10000) // > 10GB
        {
            priority += 20;
        }
        else if (metrics.ProfileSizeMB > 5000) // > 5GB
        {
            priority += 10;
        }
        else if (metrics.ProfileSizeMB > 1000) // > 1GB
        {
            priority += 5;
        }

        // Adjust based on recent activity
        var daysSinceActivity = (DateTime.UtcNow - metrics.LastActivityTime).TotalDays;
        if (daysSinceActivity < 1)
        {
            priority += 20;
        }
        else if (daysSinceActivity < 7)
        {
            priority += 10;
        }
        else if (daysSinceActivity < 30)
        {
            priority += 5;
        }

        // Currently logged in users get highest priority
        if (metrics.IsLoaded)
        {
            priority += 30;
        }

        // Domain users might have higher priority
        if (profile.ProfileType == ProfileType.Domain || profile.ProfileType == ProfileType.AzureAD)
        {
            priority += 10;
        }

        return Math.Max(1, Math.Min(priority, 999)); // Clamp between 1-999
    }

    /// <summary>
    /// Saves classification result to database
    /// </summary>
    private async Task SaveClassificationAsync(ProfileClassificationResult result, CancellationToken cancellationToken)
    {
        if (_stateManager == null)
        {
            return;
        }

        try
        {
            // Get previous classification for history
            var previousRecord = await _stateManager.GetUserClassificationAsync(result.UserId, cancellationToken);
            var previousClassification = previousRecord?.Classification;

            // Save current classification
            await _stateManager.SaveUserClassificationAsync(
                result.UserId,
                result.Classification,
                result.Confidence,
                result.Reason,
                result.RuleSetName,
                cancellationToken);

            // Save history if classification changed
            if (previousClassification != result.Classification)
            {
                var activitySnapshot = new Dictionary<string, object>
                {
                    ["ProfileSizeMB"] = result.Metrics.ProfileSizeMB,
                    ["LastActivityTime"] = result.Metrics.LastActivityTime,
                    ["LastLoginTime"] = result.Metrics.LastLoginTime,
                    ["IsLoaded"] = result.Metrics.IsLoaded,
                    ["ActiveProcessCount"] = result.Metrics.ActiveProcessCount,
                    ["ActivityScore"] = result.ActivityScore ?? 0,
                    ["Confidence"] = result.Confidence
                };

                await _stateManager.SaveClassificationHistoryAsync(
                    result.UserId,
                    previousClassification,
                    result.Classification,
                    result.Reason,
                    activitySnapshot,
                    cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save classification for user {UserId}", result.UserId);
            // Don't throw - classification should still be returned even if save fails
        }
    }

    /// <summary>
    /// Applies a manual classification override
    /// </summary>
    public async Task<OverrideResult> ApplyManualOverrideAsync(
        string userId,
        ProfileClassification classification,
        string overrideBy,
        string reason,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default)
    {
        if (_overrideManager == null)
        {
            return new OverrideResult
            {
                UserId = userId,
                Success = false,
                Error = "Override manager not available"
            };
        }

        return await _overrideManager.ApplyOverrideAsync(
            userId, classification, overrideBy, reason, expiryDate, cancellationToken);
    }

    /// <summary>
    /// Gets classification history for a user
    /// </summary>
    public async Task<IEnumerable<ClassificationHistoryEntry>> GetClassificationHistoryAsync(
        string userId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        if (_stateManager == null)
        {
            return Enumerable.Empty<ClassificationHistoryEntry>();
        }

        return await _stateManager.GetClassificationHistoryAsync(userId, limit, cancellationToken);
    }
}

/// <summary>
/// Configuration for profile classification rules
/// </summary>
public class ProfileClassificationConfig
{
    public TimeSpan ActiveThreshold { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan InactiveThreshold { get; set; } = TimeSpan.FromDays(90);
    public long MinimumActiveSizeMB { get; set; } = 100; // 100MB
    public long MinimumBackupSizeMB { get; set; } = 50; // 50MB
    public bool BackupInactiveProfiles { get; set; } = true;
    public string? RuleSetName { get; set; } = "Standard";

    public static ProfileClassificationConfig Default => new();
}

/// <summary>
/// Result of profile classification
/// </summary>
public class ProfileClassificationResult
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public ProfileClassification Classification { get; set; }
    public bool RequiresBackup { get; set; }
    public int BackupPriority { get; set; }
    public string Reason { get; set; } = string.Empty;
    public ProfileMetrics Metrics { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public double Confidence { get; set; } = 1.0;
    public bool IsOverridden { get; set; }
    public string? RuleSetName { get; set; }
    public int? ActivityScore { get; set; }
}
