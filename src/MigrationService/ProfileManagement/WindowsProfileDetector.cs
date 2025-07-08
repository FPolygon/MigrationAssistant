using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Detects and enumerates Windows user profiles
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProfileDetector
{
    private readonly ILogger<WindowsProfileDetector> _logger;
    private readonly IWindowsProfileRegistry _profileRegistry;

    public WindowsProfileDetector(
        ILogger<WindowsProfileDetector> logger,
        IWindowsProfileRegistry profileRegistry)
    {
        _logger = logger;
        _profileRegistry = profileRegistry;
    }

    /// <summary>
    /// Discovers all Windows user profiles on the system
    /// </summary>
    public async Task<List<UserProfile>> DiscoverProfilesAsync(bool includeSystemAccounts = false, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => DiscoverProfiles(includeSystemAccounts), cancellationToken);
    }

    private List<UserProfile> DiscoverProfiles(bool includeSystemAccounts)
    {
        _logger.LogInformation("Starting Windows profile discovery");
        var profiles = new List<UserProfile>();

        try
        {
            // Get all profiles from the registry
            var registryProfiles = _profileRegistry.EnumerateProfiles();
            _logger.LogDebug("Found {Count} profiles in registry", registryProfiles.Count);

            foreach (var regProfile in registryProfiles)
            {
                try
                {
                    // Skip system accounts if requested
                    if (!includeSystemAccounts && regProfile.IsSystemAccount)
                    {
                        _logger.LogDebug("Skipping system account: {Sid}", regProfile.Sid);
                        continue;
                    }

                    // Skip corrupted profiles
                    if (regProfile.IsCorrupted)
                    {
                        _logger.LogWarning("Skipping corrupted profile: {Sid} at {Path}", 
                            regProfile.Sid, regProfile.ProfilePath);
                        continue;
                    }

                    // Convert to UserProfile model
                    var userProfile = ConvertToUserProfile(regProfile);
                    if (userProfile != null)
                    {
                        profiles.Add(userProfile);
                        _logger.LogDebug("Discovered profile: {UserName} ({Sid})", 
                            userProfile.UserName, userProfile.UserId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process profile: {Sid}", regProfile.Sid);
                }
            }

            _logger.LogInformation("Profile discovery completed. Found {Count} user profiles", profiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover Windows profiles");
        }

        return profiles;
    }

    /// <summary>
    /// Converts registry profile info to UserProfile model
    /// </summary>
    private UserProfile? ConvertToUserProfile(ProfileRegistryInfo regProfile)
    {
        try
        {
            // Determine profile type
            var accountType = _profileRegistry.GetAccountType(regProfile.Sid, regProfile.UserName);
            var profileType = MapAccountTypeToProfileType(accountType);

            // Extract domain and username
            string? domainName = null;
            string userName = regProfile.UserName;

            if (userName.Contains('\\'))
            {
                var parts = userName.Split('\\', 2);
                domainName = parts[0];
                userName = parts[1];
            }

            // Get last login time
            var lastLoginTime = GetLastLoginTime(regProfile);

            var profile = new UserProfile
            {
                UserId = regProfile.Sid,
                UserName = userName,
                DomainName = domainName,
                ProfilePath = regProfile.ProfilePath,
                ProfileType = profileType,
                LastLoginTime = lastLoginTime,
                IsActive = false, // Will be determined by ProfileActivityAnalyzer
                ProfileSizeBytes = 0, // Will be calculated by ProfileActivityAnalyzer
                RequiresBackup = !regProfile.IsSystemAccount && !regProfile.IsTemporary,
                BackupPriority = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            return profile;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert registry profile to UserProfile: {Sid}", regProfile.Sid);
            return null;
        }
    }

    /// <summary>
    /// Maps Windows account type to profile type
    /// </summary>
    private static ProfileType MapAccountTypeToProfileType(ProfileAccountType accountType)
    {
        return accountType switch
        {
            ProfileAccountType.Local => ProfileType.Local,
            ProfileAccountType.Domain => ProfileType.Domain,
            ProfileAccountType.AzureAD => ProfileType.AzureAD,
            _ => ProfileType.Local
        };
    }

    /// <summary>
    /// Determines the last login time for a profile
    /// </summary>
    private DateTime GetLastLoginTime(ProfileRegistryInfo regProfile)
    {
        // Use the last load time if available
        if (regProfile.LastLoadTime.HasValue)
        {
            return regProfile.LastLoadTime.Value;
        }

        // Fallback to checking the profile directory modification time
        try
        {
            if (Directory.Exists(regProfile.ProfilePath))
            {
                var dirInfo = new DirectoryInfo(regProfile.ProfilePath);
                return dirInfo.LastWriteTimeUtc;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get directory info for profile: {Path}", regProfile.ProfilePath);
        }

        // Default to a very old date if we can't determine
        return DateTime.UtcNow.AddYears(-10);
    }

    /// <summary>
    /// Gets detailed information about a specific profile
    /// </summary>
    public async Task<UserProfile?> GetProfileBySidAsync(string sid, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                var registryProfiles = _profileRegistry.EnumerateProfiles();
                var regProfile = registryProfiles.FirstOrDefault(p => 
                    p.Sid.Equals(sid, StringComparison.OrdinalIgnoreCase));

                if (regProfile == null)
                {
                    _logger.LogWarning("Profile not found for SID: {Sid}", sid);
                    return null;
                }

                return ConvertToUserProfile(regProfile);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get profile for SID: {Sid}", sid);
                return null;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if a profile path exists and is accessible
    /// </summary>
    public bool IsProfileAccessible(string profilePath)
    {
        try
        {
            if (!Directory.Exists(profilePath))
                return false;

            // Try to enumerate at least one file to verify access
            var _ = Directory.EnumerateFiles(profilePath).FirstOrDefault();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("Access denied to profile path: {Path}", profilePath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check profile accessibility: {Path}", profilePath);
            return false;
        }
    }
}