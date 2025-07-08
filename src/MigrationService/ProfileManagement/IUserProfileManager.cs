using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Manages Windows user profile detection, enumeration, and classification
/// </summary>
public interface IUserProfileManager
{
    /// <summary>
    /// Discovers and returns all user profiles on the system
    /// </summary>
    /// <param name="includeSystemAccounts">Whether to include system/service accounts</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered user profiles</returns>
    Task<List<UserProfile>> GetAllProfilesAsync(bool includeSystemAccounts = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific user profile by SID
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>User profile if found, null otherwise</returns>
    Task<UserProfile?> GetProfileAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if a user profile is considered active based on activity criteria
    /// </summary>
    /// <param name="profile">The user profile to check</param>
    /// <returns>True if the profile is active, false otherwise</returns>
    Task<bool> IsActiveUserAsync(UserProfile profile);

    /// <summary>
    /// Calculates detailed metrics for a user profile
    /// </summary>
    /// <param name="profile">The user profile to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Profile metrics including size, activity, etc.</returns>
    Task<ProfileMetrics> CalculateMetricsAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the classification and status of a user profile
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="isActive">Whether the profile is active</param>
    /// <param name="requiresBackup">Whether the profile requires backup</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateProfileStatusAsync(string userSid, bool isActive, bool requiresBackup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes profile information from the Windows registry
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of profiles updated</returns>
    Task<int> RefreshProfilesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active user profiles that require backup
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active profiles requiring backup</returns>
    Task<List<UserProfile>> GetActiveProfilesRequiringBackupAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Detailed metrics about a user profile
/// </summary>
public class ProfileMetrics
{
    /// <summary>
    /// Total size of the profile in bytes
    /// </summary>
    public long ProfileSizeBytes { get; set; }

    /// <summary>
    /// Size of the profile in MB for display
    /// </summary>
    public long ProfileSizeMB => ProfileSizeBytes / (1024 * 1024);

    /// <summary>
    /// Last time the user logged in
    /// </summary>
    public DateTime LastLoginTime { get; set; }

    /// <summary>
    /// Last time any file in the profile was modified
    /// </summary>
    public DateTime LastActivityTime { get; set; }

    /// <summary>
    /// Number of active processes owned by the user
    /// </summary>
    public int ActiveProcessCount { get; set; }

    /// <summary>
    /// Whether the profile has had recent activity (within threshold)
    /// </summary>
    public bool HasRecentActivity { get; set; }

    /// <summary>
    /// Whether the profile directory exists and is accessible
    /// </summary>
    public bool IsAccessible { get; set; }

    /// <summary>
    /// Whether the profile is currently loaded in the registry
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Whether the user has an active session (logged in)
    /// </summary>
    public bool HasActiveSession { get; set; }

    /// <summary>
    /// Classification of the profile based on activity and size
    /// </summary>
    public ProfileClassification Classification { get; set; }

    /// <summary>
    /// Any errors encountered while analyzing the profile
    /// </summary>
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Profile classification based on activity and characteristics
/// </summary>
public enum ProfileClassification
{
    /// <summary>
    /// Active user profile that should be backed up
    /// </summary>
    Active,

    /// <summary>
    /// Inactive profile that may not need immediate backup
    /// </summary>
    Inactive,

    /// <summary>
    /// System or service account that should not be backed up
    /// </summary>
    System,

    /// <summary>
    /// Profile with errors or corruption
    /// </summary>
    Corrupted,

    /// <summary>
    /// Temporary profile (e.g., with .TEMP suffix)
    /// </summary>
    Temporary,

    /// <summary>
    /// Unknown or unclassified profile
    /// </summary>
    Unknown
}