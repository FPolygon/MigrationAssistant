using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.OneDrive.Native;
using MigrationTool.Service.ProfileManagement;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Calculates backup space requirements for user data
/// </summary>
[SupportedOSPlatform("windows")]
public class BackupRequirementsCalculator : IBackupRequirementsCalculator
{
    private readonly ILogger<BackupRequirementsCalculator> _logger;
    private readonly IFileSystemService _fileSystemService;
    private readonly IUserProfileManager _profileManager;
    private readonly IStateManager _stateManager;
    private readonly ServiceConfiguration _configuration;

    // File type categories for compression estimation
    private static readonly Dictionary<string, double> CompressionFactors = new(StringComparer.OrdinalIgnoreCase)
    {
        // Highly compressible text files
        { ".txt", 0.3 }, { ".csv", 0.2 }, { ".log", 0.2 }, { ".xml", 0.3 }, { ".json", 0.3 },
        
        // Office documents (already compressed)
        { ".docx", 0.9 }, { ".xlsx", 0.8 }, { ".pptx", 0.9 }, { ".pdf", 0.95 },
        
        // Media files (already compressed)
        { ".jpg", 0.98 }, { ".jpeg", 0.98 }, { ".png", 0.95 }, { ".mp4", 0.99 }, { ".mp3", 0.99 },
        { ".avi", 0.99 }, { ".mov", 0.99 }, { ".mkv", 0.99 },
        
        // Archive files (already compressed)
        { ".zip", 0.99 }, { ".rar", 0.99 }, { ".7z", 0.99 }, { ".gz", 0.99 },
        
        // Binary executables and libraries
        { ".exe", 0.7 }, { ".dll", 0.7 }, { ".msi", 0.85 },
        
        // Default for unknown types
        { "*", 0.7 }
    };

    // Important folders to analyze
    private static readonly string[] ImportantFolders = {
        "Desktop", "Documents", "Pictures", "Downloads", "Videos", "Music",
        "Favorites", "Links", "Contacts", "Searches"
    };

    // AppData subfolders of interest
    private static readonly string[] AppDataFolders = {
        @"Local\Microsoft\Outlook",
        @"Roaming\Microsoft\Outlook",
        @"Local\Google\Chrome",
        @"Roaming\Mozilla\Firefox",
        @"Local\Microsoft\Edge",
        @"Roaming\Microsoft\Signatures",
        @"Local\Packages",
        @"LocalLow"
    };

    public BackupRequirementsCalculator(
        ILogger<BackupRequirementsCalculator> logger,
        IFileSystemService fileSystemService,
        IUserProfileManager profileManager,
        IStateManager stateManager,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _fileSystemService = fileSystemService;
        _profileManager = profileManager;
        _stateManager = stateManager;
        _configuration = configuration.Value;
    }

    /// <inheritdoc/>
    public async Task<BackupRequirements> CalculateRequiredSpaceMBAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Calculating backup requirements for user {UserSid}", userSid);

        try
        {
            var profile = await _profileManager.GetProfileAsync(userSid);
            if (profile == null)
            {
                _logger.LogWarning("User profile not found for {UserSid}", userSid);
                return new BackupRequirements
                {
                    UserId = userSid,
                    RequiredSpaceMB = -1
                };
            }

            return await EstimateBackupSizeAsync(profile, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate backup requirements for user {UserSid}", userSid);

            // Return conservative estimate on error
            return new BackupRequirements
            {
                UserId = userSid,
                RequiredSpaceMB = 10240, // 10GB conservative estimate
                EstimatedBackupSizeMB = 10240,
                CompressionFactor = _configuration.QuotaManagement.BackupCompressionFactor
            };
        }
    }

    /// <inheritdoc/>
    public async Task<BackupRequirements> EstimateBackupSizeAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Estimating backup size for profile: {ProfilePath}", profile.ProfilePath);

        var requirements = new BackupRequirements
        {
            UserId = profile.UserId,
            ProfileSizeMB = profile.ProfileSizeMB,
            CompressionFactor = _configuration.QuotaManagement.BackupCompressionFactor
        };

        try
        {
            // Get detailed size breakdown
            requirements.Breakdown = await GetSizeBreakdownAsync(profile.ProfilePath, cancellationToken);

            // Calculate estimated backup size (excluding temp files and system files)
            requirements.EstimatedBackupSizeMB = requirements.Breakdown.TotalMB - requirements.Breakdown.TemporaryFilesMB;

            // Apply compression factor and add safety margin
            var compressedSize = (long)(requirements.EstimatedBackupSizeMB * requirements.CompressionFactor);
            requirements.RequiredSpaceMB = compressedSize + _configuration.QuotaManagement.SafetyMarginMB;

            _logger.LogDebug("Backup requirements for {UserId}: {RequiredMB} MB (from {OriginalMB} MB)",
                profile.UserId, requirements.RequiredSpaceMB, requirements.EstimatedBackupSizeMB);

            return requirements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to estimate backup size for profile {ProfilePath}", profile.ProfilePath);

            // Use conservative estimate based on profile size
            requirements.EstimatedBackupSizeMB = Math.Max(profile.ProfileSizeMB, 2048); // At least 2GB
            requirements.RequiredSpaceMB = (long)(requirements.EstimatedBackupSizeMB * 1.2); // 20% buffer

            return requirements;
        }
    }

    /// <inheritdoc/>
    public async Task<double> GetCompressionFactorAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await _fileSystemService.DirectoryExistsAsync(folderPath))
            {
                return CompressionFactors["*"]; // Default factor
            }

            var files = await _fileSystemService.GetFilesAsync(folderPath, "*", SearchOption.TopDirectoryOnly);
            if (!files.Any())
            {
                return CompressionFactors["*"];
            }

            // Sample files to estimate compression
            var sampleFiles = files.Take(100).ToList(); // Sample first 100 files
            long totalSize = 0;
            double weightedCompressionSum = 0;

            foreach (var file in sampleFiles)
            {
                try
                {
                    var fileInfo = await _fileSystemService.GetFileInfoAsync(file);
                    if (fileInfo != null && fileInfo.Length > 0)
                    {
                        var extension = Path.GetExtension(file);
                        var compressionFactor = CompressionFactors.GetValueOrDefault(extension, CompressionFactors["*"]);

                        totalSize += fileInfo.Length;
                        weightedCompressionSum += fileInfo.Length * compressionFactor;
                    }
                }
                catch
                {
                    // Skip files we can't access
                    continue;
                }
            }

            if (totalSize == 0)
            {
                return CompressionFactors["*"];
            }

            return weightedCompressionSum / totalSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate compression factor for {FolderPath}", folderPath);
            return CompressionFactors["*"];
        }
    }

    /// <inheritdoc/>
    public async Task<BackupSizeBreakdown> GetSizeBreakdownAsync(string profilePath, CancellationToken cancellationToken = default)
    {
        var breakdown = new BackupSizeBreakdown();

        try
        {
            // Analyze user folders
            foreach (var folder in ImportantFolders)
            {
                var folderPath = Path.Combine(profilePath, folder);
                var folderSize = await GetFolderSizeAsync(folderPath, cancellationToken);
                breakdown.UserFilesMB += folderSize;
            }

            // Analyze AppData folders
            var appDataPath = Path.Combine(profilePath, "AppData");
            if (await _fileSystemService.DirectoryExistsAsync(appDataPath))
            {
                // Browser data
                breakdown.BrowserDataMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Local\Google\Chrome"), cancellationToken);
                breakdown.BrowserDataMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Local\Microsoft\Edge"), cancellationToken);
                breakdown.BrowserDataMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Roaming\Mozilla\Firefox"), cancellationToken);

                // Email data
                breakdown.EmailDataMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Local\Microsoft\Outlook"), cancellationToken);
                breakdown.EmailDataMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Roaming\Microsoft\Outlook"), cancellationToken);

                // System configuration
                breakdown.SystemConfigMB += await GetFolderSizeAsync(
                    Path.Combine(appDataPath, @"Roaming\Microsoft\Signatures"), cancellationToken);

                // Remaining AppData
                var totalAppDataSize = await GetFolderSizeAsync(appDataPath, cancellationToken);
                breakdown.AppDataMB = totalAppDataSize - breakdown.BrowserDataMB -
                                    breakdown.EmailDataMB - breakdown.SystemConfigMB;
            }

            // Identify temporary files
            breakdown.TemporaryFilesMB = await CalculateTemporaryFilesSizeAsync(profilePath, cancellationToken);

            _logger.LogDebug("Size breakdown for {ProfilePath}: User={UserFilesMB}MB, AppData={AppDataMB}MB, " +
                           "Browser={BrowserDataMB}MB, Email={EmailDataMB}MB, System={SystemConfigMB}MB, Temp={TemporaryFilesMB}MB",
                           profilePath, breakdown.UserFilesMB, breakdown.AppDataMB, breakdown.BrowserDataMB,
                           breakdown.EmailDataMB, breakdown.SystemConfigMB, breakdown.TemporaryFilesMB);

            return breakdown;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get size breakdown for {ProfilePath}", profilePath);
            return breakdown;
        }
    }

    /// <inheritdoc/>
    public bool ValidateRequirements(BackupRequirements requirements)
    {
        // Basic validation checks
        if (requirements.RequiredSpaceMB < 0)
        {
            _logger.LogWarning("Invalid negative space requirement: {RequiredMB} MB", requirements.RequiredSpaceMB);
            return false;
        }

        // Check if requirements are unreasonably large (>500GB)
        const long maxReasonableSize = 500 * 1024; // 500GB in MB
        if (requirements.RequiredSpaceMB > maxReasonableSize)
        {
            _logger.LogWarning("Backup requirements seem unreasonably large: {RequiredMB} MB", requirements.RequiredSpaceMB);
            return false;
        }

        // Check compression factor is reasonable
        if (requirements.CompressionFactor < 0.1 || requirements.CompressionFactor > 1.0)
        {
            _logger.LogWarning("Invalid compression factor: {CompressionFactor}", requirements.CompressionFactor);
            return false;
        }

        return true;
    }

    private async Task<long> GetFolderSizeAsync(string folderPath, CancellationToken cancellationToken)
    {
        try
        {
            if (!await _fileSystemService.DirectoryExistsAsync(folderPath))
            {
                return 0;
            }

            var files = await _fileSystemService.GetFilesAsync(folderPath, "*", SearchOption.AllDirectories);
            long totalSize = 0;

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = await _fileSystemService.GetFileInfoAsync(file);
                    if (fileInfo != null)
                    {
                        totalSize += fileInfo.Length;
                    }
                }
                catch
                {
                    // Skip files we can't access
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return totalSize / (1024 * 1024); // Convert to MB
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate size for folder {FolderPath}", folderPath);
            return 0;
        }
    }

    private async Task<long> CalculateTemporaryFilesSizeAsync(string profilePath, CancellationToken cancellationToken)
    {
        try
        {
            long tempSize = 0;

            // Common temporary file locations
            var tempPaths = new[]
            {
                Path.Combine(profilePath, @"AppData\Local\Temp"),
                Path.Combine(profilePath, @"AppData\Local\Microsoft\Windows\INetCache"),
                Path.Combine(profilePath, @"AppData\Local\Microsoft\Windows\Temporary Internet Files"),
                Path.Combine(profilePath, @"AppData\Local\Microsoft\Edge\User Data\Default\Cache"),
                Path.Combine(profilePath, @"AppData\Local\Google\Chrome\User Data\Default\Cache")
            };

            foreach (var tempPath in tempPaths)
            {
                tempSize += await GetFolderSizeAsync(tempPath, cancellationToken);
            }

            return tempSize;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate temporary files size for {ProfilePath}", profilePath);
            return 0;
        }
    }
}
