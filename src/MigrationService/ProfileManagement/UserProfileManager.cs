using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Main implementation of user profile management functionality
/// </summary>
[SupportedOSPlatform("windows")]
public class UserProfileManager : IUserProfileManager
{
    private readonly ILogger<UserProfileManager> _logger;
    private readonly IStateManager _stateManager;
    private readonly WindowsProfileDetector _profileDetector;
    private readonly IProfileActivityAnalyzer _activityAnalyzer;
    private readonly IProfileClassifier _profileClassifier;
    private readonly object _refreshLock = new();
    private DateTime _lastRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public UserProfileManager(
        ILogger<UserProfileManager> logger,
        IStateManager stateManager,
        WindowsProfileDetector profileDetector,
        IProfileActivityAnalyzer activityAnalyzer,
        IProfileClassifier profileClassifier)
    {
        _logger = logger;
        _stateManager = stateManager;
        _profileDetector = profileDetector;
        _activityAnalyzer = activityAnalyzer;
        _profileClassifier = profileClassifier;
    }

    /// <inheritdoc/>
    public async Task<List<UserProfile>> GetAllProfilesAsync(bool includeSystemAccounts = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all user profiles (includeSystem: {IncludeSystem})", includeSystemAccounts);

        try
        {
            // Check if we need to refresh profiles
            if (ShouldRefreshProfiles())
            {
                await RefreshProfilesAsync(cancellationToken);
            }

            // Get profiles from state manager
            var profiles = await _stateManager.GetUserProfilesAsync(cancellationToken);

            // Filter out system accounts if requested
            if (!includeSystemAccounts)
            {
                profiles = profiles.Where(p => 
                    p.ProfileType != ProfileType.Local || 
                    !p.UserId.StartsWith("S-1-5-18") && 
                    !p.UserId.StartsWith("S-1-5-19") && 
                    !p.UserId.StartsWith("S-1-5-20"))
                    .ToList();
            }

            _logger.LogInformation("Retrieved {Count} user profiles", profiles.Count());
            return profiles.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all profiles");
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<UserProfile?> GetProfileAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting profile for SID: {Sid}", userSid);

        try
        {
            // Try to get from state manager first
            var profiles = await _stateManager.GetUserProfilesAsync(cancellationToken);
            var profile = profiles.FirstOrDefault(p => p.UserId.Equals(userSid, StringComparison.OrdinalIgnoreCase));

            if (profile == null)
            {
                _logger.LogDebug("Profile not found in state, checking Windows registry");
                // Try to discover from Windows
                profile = await _profileDetector.GetProfileBySidAsync(userSid, cancellationToken);
                
                if (profile != null)
                {
                    // Analyze and save the newly discovered profile
                    var metrics = await _activityAnalyzer.AnalyzeProfileAsync(profile, cancellationToken);
                    var classification = await _profileClassifier.ClassifyProfileAsync(profile, metrics, cancellationToken);
                    
                    profile.IsActive = classification.Classification == ProfileClassification.Active;
                    profile.RequiresBackup = classification.RequiresBackup;
                    profile.BackupPriority = classification.BackupPriority;
                    profile.ProfileSizeBytes = metrics.ProfileSizeBytes;
                    
                    await _stateManager.UpdateUserProfileAsync(profile, cancellationToken);
                }
            }

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get profile for SID: {Sid}", userSid);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsActiveUserAsync(UserProfile profile)
    {
        try
        {
            var metrics = await _activityAnalyzer.AnalyzeProfileAsync(profile);
            return _activityAnalyzer.IsProfileActive(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine if user is active: {UserName}", profile.UserName);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<ProfileMetrics> CalculateMetricsAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Calculating metrics for profile: {UserName}", profile.UserName);

        try
        {
            return await _activityAnalyzer.AnalyzeProfileAsync(profile, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate metrics for profile: {UserName}", profile.UserName);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task UpdateProfileStatusAsync(string userSid, bool isActive, bool requiresBackup, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Updating profile status for {Sid}: Active={IsActive}, RequiresBackup={RequiresBackup}", 
            userSid, isActive, requiresBackup);

        try
        {
            var profile = await GetProfileAsync(userSid, cancellationToken);
            if (profile == null)
            {
                _logger.LogWarning("Cannot update status for non-existent profile: {Sid}", userSid);
                return;
            }

            profile.IsActive = isActive;
            profile.RequiresBackup = requiresBackup;
            profile.UpdatedAt = DateTime.UtcNow;

            await _stateManager.UpdateUserProfileAsync(profile, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update profile status for SID: {Sid}", userSid);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<int> RefreshProfilesAsync(CancellationToken cancellationToken = default)
    {
        lock (_refreshLock)
        {
            if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromSeconds(30))
            {
                _logger.LogDebug("Skipping refresh - too soon since last refresh");
                return 0;
            }
            _lastRefresh = DateTime.UtcNow;
        }

        _logger.LogInformation("Starting profile refresh");
        var updatedCount = 0;

        try
        {
            // Discover all profiles from Windows
            var windowsProfiles = await _profileDetector.DiscoverProfilesAsync(includeSystemAccounts: true, cancellationToken);
            _logger.LogDebug("Discovered {Count} profiles from Windows", windowsProfiles.Count);

            // Get existing profiles from state
            var existingProfiles = await _stateManager.GetUserProfilesAsync(cancellationToken);
            var existingProfilesMap = existingProfiles.ToDictionary(p => p.UserId, StringComparer.OrdinalIgnoreCase);

            // Process each discovered profile
            foreach (var profile in windowsProfiles)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Analyze profile metrics
                    var metrics = await _activityAnalyzer.AnalyzeProfileAsync(profile, cancellationToken);
                    
                    // Classify profile
                    var classification = await _profileClassifier.ClassifyProfileAsync(profile, metrics, cancellationToken);
                    
                    // Update profile properties
                    profile.IsActive = classification.Classification == ProfileClassification.Active;
                    profile.RequiresBackup = classification.RequiresBackup;
                    profile.BackupPriority = classification.BackupPriority;
                    profile.ProfileSizeBytes = metrics.ProfileSizeBytes;
                    profile.LastLoginTime = metrics.LastLoginTime;

                    // Check if this is a new or updated profile
                    if (existingProfilesMap.TryGetValue(profile.UserId, out var existingProfile))
                    {
                        // Update only if something changed
                        if (ProfileHasChanged(existingProfile, profile))
                        {
                            profile.CreatedAt = existingProfile.CreatedAt; // Preserve creation time
                            profile.UpdatedAt = DateTime.UtcNow;
                            await _stateManager.UpdateUserProfileAsync(profile, cancellationToken);
                            updatedCount++;
                            _logger.LogDebug("Updated profile: {UserName} ({UserId})", profile.UserName, profile.UserId);
                        }
                    }
                    else
                    {
                        // New profile
                        profile.CreatedAt = DateTime.UtcNow;
                        profile.UpdatedAt = DateTime.UtcNow;
                        await _stateManager.UpdateUserProfileAsync(profile, cancellationToken);
                        updatedCount++;
                        _logger.LogInformation("Discovered new profile: {UserName} ({UserId})", profile.UserName, profile.UserId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process profile: {UserName} ({UserId})", profile.UserName, profile.UserId);
                }
            }

            // Check for removed profiles
            var windowsProfileSids = new HashSet<string>(windowsProfiles.Select(p => p.UserId), StringComparer.OrdinalIgnoreCase);
            foreach (var existingProfile in existingProfiles)
            {
                if (!windowsProfileSids.Contains(existingProfile.UserId))
                {
                    _logger.LogWarning("Profile no longer exists in Windows: {UserName} ({UserId})", 
                        existingProfile.UserName, existingProfile.UserId);
                    // Note: We don't delete profiles, but you might want to mark them as deleted
                }
            }

            _logger.LogInformation("Profile refresh completed. Updated {Count} profiles", updatedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh profiles");
            throw;
        }

        return updatedCount;
    }

    /// <inheritdoc/>
    public async Task<List<UserProfile>> GetActiveProfilesRequiringBackupAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting active profiles requiring backup");

        try
        {
            var allProfiles = await GetAllProfilesAsync(includeSystemAccounts: false, cancellationToken);
            var activeProfiles = allProfiles
                .Where(p => p.IsActive && p.RequiresBackup)
                .OrderByDescending(p => p.BackupPriority)
                .ThenBy(p => p.UserName)
                .ToList();

            _logger.LogInformation("Found {Count} active profiles requiring backup", activeProfiles.Count);
            return activeProfiles;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active profiles requiring backup");
            throw;
        }
    }

    /// <summary>
    /// Determines if a profile should be refreshed from cache
    /// </summary>
    private bool ShouldRefreshProfiles()
    {
        lock (_refreshLock)
        {
            return DateTime.UtcNow - _lastRefresh > _cacheExpiry;
        }
    }

    /// <summary>
    /// Checks if a profile has changed
    /// </summary>
    private static bool ProfileHasChanged(UserProfile existing, UserProfile updated)
    {
        return existing.UserName != updated.UserName ||
               existing.ProfilePath != updated.ProfilePath ||
               existing.ProfileType != updated.ProfileType ||
               existing.IsActive != updated.IsActive ||
               existing.RequiresBackup != updated.RequiresBackup ||
               existing.BackupPriority != updated.BackupPriority ||
               Math.Abs(existing.ProfileSizeBytes - updated.ProfileSizeBytes) > 1024 * 1024 || // 1MB difference
               Math.Abs((existing.LastLoginTime - updated.LastLoginTime).TotalDays) > 1;
    }
}