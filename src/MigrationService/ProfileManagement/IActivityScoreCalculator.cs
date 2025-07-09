using System.Runtime.Versioning;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Interface for calculating comprehensive activity scores for user profiles using weighted algorithms
/// </summary>
[SupportedOSPlatform("windows")]
public interface IActivityScoreCalculator
{
    /// <summary>
    /// Calculates a comprehensive activity score for a user profile
    /// </summary>
    Task<ActivityScoreResult> CalculateScoreAsync(
        UserProfile profile,
        ProfileMetrics metrics,
        UserActivityData? activityData = null,
        UserProcessInfo? processInfo = null,
        FileActivityReport? fileActivity = null,
        CancellationToken cancellationToken = default);
}
