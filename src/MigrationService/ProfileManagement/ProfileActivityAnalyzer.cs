using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Analyzes user profile activity and calculates metrics
/// </summary>
[SupportedOSPlatform("windows")]
public class ProfileActivityAnalyzer : IProfileActivityAnalyzer
{
    private readonly ILogger<ProfileActivityAnalyzer> _logger;
    private readonly TimeSpan _recentActivityThreshold;
    private readonly long _minimumActiveSizeBytes;
    private readonly WindowsActivityDetector? _activityDetector;
    private readonly ProcessOwnershipDetector? _processDetector;

    // Common folders to check for activity
    private static readonly string[] ActivityCheckFolders = new[]
    {
        "Desktop",
        "Documents",
        "Downloads",
        "Pictures",
        "Videos",
        @"AppData\Local",
        @"AppData\Roaming"
    };

    // Folders to exclude from size calculation
    private static readonly string[] ExcludedFolders = new[]
    {
        @"AppData\Local\Microsoft\Windows\INetCache",
        @"AppData\Local\Microsoft\Windows\WebCache",
        @"AppData\Local\Temp",
        @"AppData\Local\Microsoft\Windows\Temporary Internet Files",
        @"AppData\Local\Packages",
        @"AppData\Local\Microsoft\WindowsApps"
    };

    public ProfileActivityAnalyzer(
        ILogger<ProfileActivityAnalyzer> logger,
        WindowsActivityDetector? activityDetector = null,
        ProcessOwnershipDetector? processDetector = null,
        TimeSpan? recentActivityThreshold = null,
        long? minimumActiveSizeBytes = null)
    {
        _logger = logger;
        _activityDetector = activityDetector;
        _processDetector = processDetector;
        _recentActivityThreshold = recentActivityThreshold ?? TimeSpan.FromDays(30);
        _minimumActiveSizeBytes = minimumActiveSizeBytes ?? 100L * 1024 * 1024; // 100MB default
    }

    /// <summary>
    /// Analyzes a user profile and calculates detailed metrics
    /// </summary>
    public async Task<ProfileMetrics> AnalyzeProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Analyzing profile metrics for user: {UserName} ({UserId})", profile.UserName, profile.UserId);

        var metrics = new ProfileMetrics
        {
            LastLoginTime = profile.LastLoginTime,
            Classification = ProfileClassification.Unknown
        };

        try
        {
            // Check if profile directory exists and is accessible
            if (!Directory.Exists(profile.ProfilePath))
            {
                _logger.LogWarning("Profile directory does not exist: {Path}", profile.ProfilePath);
                metrics.IsAccessible = false;
                metrics.Classification = ProfileClassification.Corrupted;
                metrics.Errors.Add($"Profile directory not found: {profile.ProfilePath}");
                return metrics;
            }

            metrics.IsAccessible = await CheckAccessibilityAsync(profile.ProfilePath, cancellationToken);
            if (!metrics.IsAccessible)
            {
                metrics.Classification = ProfileClassification.Corrupted;
                metrics.Errors.Add($"Profile directory not accessible: {profile.ProfilePath}");
                return metrics;
            }

            // Analyze profile in parallel tasks
            var tasks = new List<Task>
            {
                Task.Run(() => CalculateProfileSize(profile.ProfilePath, metrics, cancellationToken), cancellationToken),
                Task.Run(() => FindLastActivityTime(profile.ProfilePath, metrics, cancellationToken), cancellationToken),
                Task.Run(async () => await CheckActiveProcessesEnhancedAsync(profile.UserId, metrics, cancellationToken), cancellationToken),
                Task.Run(() => CheckProfileLoaded(profile.UserId, metrics), cancellationToken),
                Task.Run(async () => await DetectEnhancedActivityAsync(profile.UserId, metrics, cancellationToken), cancellationToken)
            };

            await Task.WhenAll(tasks);

            // Determine if profile has recent activity
            var activityAge = DateTime.UtcNow - metrics.LastActivityTime;
            metrics.HasRecentActivity = activityAge <= _recentActivityThreshold;

            // Log summary
            _logger.LogInformation(
                "Profile analysis complete for {UserName}: Size={SizeMB}MB, LastActivity={LastActivity}, ActiveProcesses={ProcessCount}, RecentActivity={HasRecent}",
                profile.UserName,
                metrics.ProfileSizeMB,
                metrics.LastActivityTime,
                metrics.ActiveProcessCount,
                metrics.HasRecentActivity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze profile: {UserName}", profile.UserName);
            metrics.Errors.Add($"Analysis failed: {ex.Message}");
        }

        return metrics;
    }

    /// <summary>
    /// Checks if a profile directory is accessible
    /// </summary>
    private async Task<bool> CheckAccessibilityAsync(string profilePath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Try to enumerate files to verify access
                var _ = Directory.EnumerateFiles(profilePath, "*", new EnumerationOptions
                {
                    IgnoreInaccessible = true,
                    RecurseSubdirectories = false,
                    MaxRecursionDepth = 0
                }).FirstOrDefault();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogWarning("Access denied to profile: {Path}", profilePath);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check profile accessibility: {Path}", profilePath);
                return false;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Calculates the total size of a user profile
    /// </summary>
    private void CalculateProfileSize(string profilePath, ProfileMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            long totalSize = 0;
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
            };

            var profileDir = new DirectoryInfo(profilePath);
            
            // Calculate size recursively, excluding certain folders
            totalSize = CalculateDirectorySize(profileDir, ExcludedFolders, options, cancellationToken);

            metrics.ProfileSizeBytes = totalSize;
            _logger.LogDebug("Profile size calculated: {SizeMB}MB for {Path}", totalSize / (1024 * 1024), profilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate profile size for: {Path}", profilePath);
            metrics.Errors.Add($"Size calculation failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Recursively calculates directory size with exclusions
    /// </summary>
    private long CalculateDirectorySize(DirectoryInfo directory, string[] excludedFolders, EnumerationOptions options, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return 0;

        long size = 0;

        try
        {
            // Check if this directory should be excluded
            foreach (var excluded in excludedFolders)
            {
                // Normalize paths for comparison
                var excludedPath = excluded.Replace('/', '\\');
                var dirPath = directory.FullName;
                
                // Check if the directory path contains the excluded path
                if (dirPath.Contains(excludedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogTrace("Excluding directory from size calculation: {Directory}", dirPath);
                    return 0;
                }
            }

            // Add file sizes
            // Create separate options for file enumeration (without recursion)
            var fileOptions = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                AttributesToSkip = options.AttributesToSkip
            };
            
            foreach (var file in directory.EnumerateFiles("*", fileOptions))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    size += file.Length;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Could not get size for file: {File}", file.FullName);
                }
            }

            // Recursively process subdirectories
            foreach (var subDir in directory.EnumerateDirectories("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = options.AttributesToSkip }))
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                size += CalculateDirectorySize(subDir, excludedFolders, options, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error calculating size for directory: {Directory}", directory.FullName);
        }

        return size;
    }

    /// <summary>
    /// Finds the most recent activity time in the profile
    /// </summary>
    private void FindLastActivityTime(string profilePath, ProfileMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            var mostRecentTime = metrics.LastLoginTime;

            // Check specific folders for recent activity
            foreach (var folder in ActivityCheckFolders)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var folderPath = Path.Combine(profilePath, folder);
                if (!Directory.Exists(folderPath))
                    continue;

                try
                {
                    // Check directory modification time
                    var dirInfo = new DirectoryInfo(folderPath);
                    if (dirInfo.LastWriteTimeUtc > mostRecentTime)
                        mostRecentTime = dirInfo.LastWriteTimeUtc;

                    // Check a few recent files
                    var recentFiles = Directory.EnumerateFiles(folderPath, "*", new EnumerationOptions
                    {
                        IgnoreInaccessible = true,
                        RecurseSubdirectories = false,
                        MaxRecursionDepth = 0
                    })
                    .Take(10) // Check only first 10 files for performance
                    .Select(f => new FileInfo(f))
                    .Where(f => f.Exists);

                    foreach (var file in recentFiles)
                    {
                        if (file.LastWriteTimeUtc > mostRecentTime)
                            mostRecentTime = file.LastWriteTimeUtc;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Error checking folder activity: {Folder}", folderPath);
                }
            }

            metrics.LastActivityTime = mostRecentTime;
            _logger.LogDebug("Last activity time found: {Time} for {Path}", mostRecentTime, profilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find last activity time for: {Path}", profilePath);
            metrics.LastActivityTime = metrics.LastLoginTime;
            metrics.Errors.Add($"Activity time check failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Enhanced check for active processes owned by the user
    /// </summary>
    private async Task CheckActiveProcessesEnhancedAsync(string userSid, ProfileMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            if (_processDetector != null)
            {
                // Use the enhanced process detector
                var processInfo = await _processDetector.GetUserProcessesAsync(userSid, cancellationToken);
                
                metrics.ActiveProcessCount = processInfo.TotalProcessCount;
                metrics.HasActiveSession = processInfo.HasExplorerProcess;
                
                // Store additional process information in metrics
                if (processInfo.TotalProcessCount > 0)
                {
                    metrics.Errors.Add($"Process detection: Found {processInfo.InteractiveProcessCount} interactive processes");
                }
                
                _logger.LogDebug("Enhanced process detection found {Count} processes for user {Sid}", 
                    processInfo.TotalProcessCount, userSid);
            }
            else
            {
                // Fallback to simple check
                CheckActiveProcessesSimple(userSid, metrics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check active processes for user: {Sid}", userSid);
            metrics.ActiveProcessCount = 0;
        }
    }

    /// <summary>
    /// Simple check for active processes (fallback)
    /// </summary>
    private void CheckActiveProcessesSimple(string userSid, ProfileMetrics metrics)
    {
        try
        {
            // Simple check if user's registry hive is loaded
            using var userKey = Microsoft.Win32.Registry.Users.OpenSubKey(userSid);
            if (userKey != null)
            {
                metrics.ActiveProcessCount = 1; // At least something is active
                metrics.IsLoaded = true;
            }
            
            _logger.LogDebug("Simple process check for user {Sid}: Registry loaded = {IsLoaded}", 
                userSid, metrics.IsLoaded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed simple process check for user: {Sid}", userSid);
            metrics.ActiveProcessCount = 0;
        }
    }

    /// <summary>
    /// Checks if the user profile is currently loaded
    /// </summary>
    private void CheckProfileLoaded(string userSid, ProfileMetrics metrics)
    {
        try
        {
            // Check if the user's registry hive is loaded
            // In a real implementation, you'd check HKU\{SID}
            using var userKey = Microsoft.Win32.Registry.Users.OpenSubKey(userSid);
            metrics.IsLoaded = userKey != null;
            
            _logger.LogDebug("Profile loaded status: {IsLoaded} for user {Sid}", metrics.IsLoaded, userSid);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if profile is loaded: {Sid}", userSid);
            metrics.IsLoaded = false;
        }
    }

    /// <summary>
    /// Determines if a profile should be considered active based on metrics
    /// </summary>
    public bool IsProfileActive(ProfileMetrics metrics)
    {
        // Profile is active if:
        // 1. It has recent activity (within threshold)
        // 2. OR it's currently loaded
        // 3. OR it has active processes
        // 4. AND it meets minimum size requirements
        // 5. AND it's accessible

        if (!metrics.IsAccessible)
            return false;

        if (metrics.ProfileSizeBytes < _minimumActiveSizeBytes)
            return false;

        return metrics.HasRecentActivity || metrics.IsLoaded || metrics.ActiveProcessCount > 0;
    }

    /// <summary>
    /// Detects enhanced activity using Windows APIs
    /// </summary>
    private async Task DetectEnhancedActivityAsync(string userSid, ProfileMetrics metrics, CancellationToken cancellationToken)
    {
        try
        {
            if (_activityDetector != null)
            {
                var activityData = await _activityDetector.GetUserActivityAsync(userSid, cancellationToken);
                
                // Update metrics with enhanced data
                if (activityData.LastInteractiveLogon > metrics.LastLoginTime)
                    metrics.LastLoginTime = activityData.LastInteractiveLogon;
                    
                if (activityData.MostRecentActivity > metrics.LastActivityTime)
                    metrics.LastActivityTime = activityData.MostRecentActivity;
                
                // Use registry loaded status
                if (activityData.IsRegistryLoaded)
                    metrics.IsLoaded = true;
                
                // Update recent activity based on comprehensive data
                var enhancedActivityAge = DateTime.UtcNow - activityData.MostRecentActivity;
                if (enhancedActivityAge <= _recentActivityThreshold)
                    metrics.HasRecentActivity = true;
                
                // Check for active session
                if (activityData.HasActiveSession)
                    metrics.HasActiveSession = true;
                
                _logger.LogDebug(
                    "Enhanced activity detection for {Sid}: LastLogin={LastLogin}, MostRecent={Recent}, HasSession={HasSession}",
                    userSid, activityData.LastInteractiveLogon, activityData.MostRecentActivity, activityData.HasActiveSession);
                
                // Add detailed activity info to errors (for debugging)
                if (activityData.LogonEvents.Any())
                {
                    var recentLogon = activityData.LogonEvents.FirstOrDefault();
                    if (recentLogon != null)
                    {
                        metrics.Errors.Add($"Last logon event: {recentLogon.EventType} at {recentLogon.EventTime}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect enhanced activity for user: {Sid}", userSid);
            metrics.Errors.Add($"Enhanced activity detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets comprehensive last login time from multiple sources
    /// </summary>
    public async Task<DateTime> GetLastLoginTimeAsync(string userSid, string profilePath, CancellationToken cancellationToken = default)
    {
        var lastLoginTime = DateTime.MinValue;

        try
        {
            // Get activity data if detector is available
            if (_activityDetector != null)
            {
                var activityData = await _activityDetector.GetUserActivityAsync(userSid, cancellationToken);
                lastLoginTime = activityData.LastInteractiveLogon;
            }

            // Check NTUSER.DAT as fallback
            if (lastLoginTime == DateTime.MinValue && !string.IsNullOrEmpty(profilePath))
            {
                var ntuserPath = Path.Combine(profilePath, "NTUSER.DAT");
                if (File.Exists(ntuserPath))
                {
                    var fileInfo = new FileInfo(ntuserPath);
                    lastLoginTime = fileInfo.LastWriteTimeUtc;
                }
            }

            _logger.LogDebug("Last login time for {Sid}: {LastLogin}", userSid, lastLoginTime);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get last login time for user: {Sid}", userSid);
        }

        return lastLoginTime;
    }

    /// <summary>
    /// Gets activity score for a profile (0-100)
    /// </summary>
    public Task<int> GetActivityScoreAsync(UserProfile profile, ProfileMetrics metrics, CancellationToken cancellationToken = default)
    {
        var score = 0;

        try
        {
            // Base score on various factors
            
            // 1. Login recency (0-40 points)
            var daysSinceLogin = (DateTime.UtcNow - metrics.LastLoginTime).TotalDays;
            if (daysSinceLogin < 1) score += 40;
            else if (daysSinceLogin < 7) score += 30;
            else if (daysSinceLogin < 30) score += 20;
            else if (daysSinceLogin < 90) score += 10;

            // 2. Active processes (0-20 points)
            if (metrics.ActiveProcessCount > 10) score += 20;
            else if (metrics.ActiveProcessCount > 5) score += 15;
            else if (metrics.ActiveProcessCount > 0) score += 10;

            // 3. Profile loaded (0-15 points)
            if (metrics.IsLoaded) score += 15;

            // 4. Recent file activity (0-15 points)
            var daysSinceActivity = (DateTime.UtcNow - metrics.LastActivityTime).TotalDays;
            if (daysSinceActivity < 1) score += 15;
            else if (daysSinceActivity < 7) score += 10;
            else if (daysSinceActivity < 30) score += 5;

            // 5. Profile size (0-10 points)
            if (metrics.ProfileSizeMB > 1000) score += 10;
            else if (metrics.ProfileSizeMB > 500) score += 7;
            else if (metrics.ProfileSizeMB > 100) score += 5;

            _logger.LogDebug("Activity score for {UserName}: {Score}/100", profile.UserName, score);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate activity score for user: {UserName}", profile.UserName);
        }

        return Task.FromResult(Math.Min(score, 100));
    }
}