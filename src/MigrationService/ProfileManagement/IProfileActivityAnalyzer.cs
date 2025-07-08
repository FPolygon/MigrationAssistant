using System.Runtime.Versioning;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Interface for analyzing user profile activity and calculating metrics
/// </summary>
[SupportedOSPlatform("windows")]
public interface IProfileActivityAnalyzer
{
    /// <summary>
    /// Analyzes a user profile and calculates detailed metrics
    /// </summary>
    Task<ProfileMetrics> AnalyzeProfileAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if a profile should be considered active based on metrics
    /// </summary>
    bool IsProfileActive(ProfileMetrics metrics);

    /// <summary>
    /// Gets comprehensive last login time from multiple sources
    /// </summary>
    Task<DateTime> GetLastLoginTimeAsync(string userSid, string profilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets activity score for a profile (0-100)
    /// </summary>
    Task<int> GetActivityScoreAsync(UserProfile profile, ProfileMetrics metrics, CancellationToken cancellationToken = default);
}