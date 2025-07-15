using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Core detection logic for OneDrive status and configuration
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveDetector
{
    private readonly ILogger<OneDriveDetector> _logger;
    private readonly IOneDriveRegistry _registry;
    private readonly OneDriveProcessDetector _processDetector;

    public OneDriveDetector(
        ILogger<OneDriveDetector> logger,
        IOneDriveRegistry registry,
        OneDriveProcessDetector processDetector)
    {
        _logger = logger;
        _registry = registry;
        _processDetector = processDetector;
    }

    /// <summary>
    /// Performs comprehensive OneDrive detection for a user
    /// </summary>
    public async Task<OneDriveStatus> DetectOneDriveStatusAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Detecting OneDrive status for user {Sid}", userSid);

        var status = new OneDriveStatus
        {
            LastChecked = DateTime.UtcNow
        };

        try
        {
            // Step 1: Check if OneDrive is installed
            status.IsInstalled = _registry.IsOneDriveInstalled();
            if (!status.IsInstalled)
            {
                _logger.LogWarning("OneDrive is not installed on the system");
                status.SyncStatus = OneDriveSyncStatus.Unknown;
                return status;
            }

            // Step 2: Check if OneDrive process is running for the user
            status.IsRunning = await _processDetector.IsOneDriveRunningForUserAsync(userSid);

            // Step 3: Get user accounts
            var accounts = await _registry.GetUserAccountsAsync(userSid);
            if (accounts.Count == 0)
            {
                _logger.LogWarning("No OneDrive accounts found for user {Sid}", userSid);
                status.IsSignedIn = false;
                status.SyncStatus = OneDriveSyncStatus.NotSignedIn;
                return status;
            }

            // Step 4: Get primary account
            var primaryAccount = accounts.FirstOrDefault(a => a.IsPrimary) ?? accounts.First();
            status.AccountInfo = primaryAccount;
            status.AccountEmail = primaryAccount.Email;
            status.SyncFolder = primaryAccount.UserFolder;
            status.IsSignedIn = !string.IsNullOrEmpty(primaryAccount.Email) &&
                                Directory.Exists(primaryAccount.UserFolder);

            if (!status.IsSignedIn)
            {
                status.SyncStatus = OneDriveSyncStatus.NotSignedIn;
                return status;
            }

            // Step 5: Check sync status
            status.SyncStatus = await DetermineSyncStatusAsync(userSid, primaryAccount, cancellationToken);

            // Step 6: Get all synced folders
            var syncedFolders = await _registry.GetSyncedFoldersAsync(userSid);
            if (primaryAccount.SyncedFolders != null)
            {
                primaryAccount.SyncedFolders.AddRange(syncedFolders);
            }
            else
            {
                primaryAccount.SyncedFolders = syncedFolders;
            }

            // Step 7: Check for authentication issues
            if (await CheckForAuthenticationIssuesAsync(userSid, primaryAccount))
            {
                status.SyncStatus = OneDriveSyncStatus.AuthenticationRequired;
                primaryAccount.HasSyncErrors = true;
                primaryAccount.SyncErrorDetails = "Authentication token expired or invalid";
            }

            _logger.LogInformation("OneDrive detection completed for user {Sid}. Status: {Status}",
                userSid, status.SyncStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to detect OneDrive status for user {Sid}", userSid);
            status.ErrorDetails = ex.Message;
            status.SyncStatus = OneDriveSyncStatus.Unknown;
        }

        return status;
    }

    /// <summary>
    /// Detects specific sync folder status
    /// </summary>
    public async Task<SyncProgress> GetSyncProgressAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        var progress = new SyncProgress
        {
            FolderPath = folderPath,
            Status = OneDriveSyncStatus.Unknown
        };

        try
        {
            if (!Directory.Exists(folderPath))
            {
                progress.Status = OneDriveSyncStatus.Error;
                progress.Errors.Add(new SyncError
                {
                    FilePath = folderPath,
                    ErrorMessage = "Folder does not exist",
                    ErrorTime = DateTime.UtcNow
                });
                return progress;
            }

            // Check for sync status icons/attributes
            var syncStatus = await CheckFolderSyncStatusAsync(folderPath, cancellationToken);
            progress.Status = syncStatus;

            // Get folder statistics
            var folderInfo = new DirectoryInfo(folderPath);
            var files = folderInfo.GetFiles("*", SearchOption.AllDirectories);

            progress.TotalFiles = files.Length;
            progress.TotalBytes = files.Sum(f => f.Length);

            // Check for OneDrive placeholder files (Files On-Demand)
            var syncedCount = 0;
            var syncedBytes = 0L;

            foreach (var file in files)
            {
                if (await IsFileSyncedAsync(file.FullName))
                {
                    syncedCount++;
                    syncedBytes += file.Length;
                }
            }

            progress.FilesSynced = syncedCount;
            progress.BytesSynced = syncedBytes;
            progress.PercentComplete = progress.TotalFiles > 0
                ? (double)progress.FilesSynced / progress.TotalFiles * 100
                : 100;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sync progress for folder {FolderPath}", folderPath);
            progress.Status = OneDriveSyncStatus.Error;
            progress.Errors.Add(new SyncError
            {
                FilePath = folderPath,
                ErrorMessage = ex.Message,
                ErrorTime = DateTime.UtcNow,
                IsRecoverable = false
            });
        }

        return progress;
    }

    #region Private Methods

    private async Task<OneDriveSyncStatus> DetermineSyncStatusAsync(
        string userSid,
        OneDriveAccountInfo account,
        CancellationToken cancellationToken)
    {
        try
        {
            // Check if sync is paused
            if (await _registry.IsSyncPausedAsync(userSid))
            {
                return OneDriveSyncStatus.Paused;
            }

            // Check for sync errors in the sync folder
            if (!string.IsNullOrEmpty(account.UserFolder) && Directory.Exists(account.UserFolder))
            {
                var syncStatus = await CheckFolderSyncStatusAsync(account.UserFolder, cancellationToken);
                if (syncStatus != OneDriveSyncStatus.Unknown)
                {
                    return syncStatus;
                }
            }

            // If OneDrive is running and no errors detected, assume up to date
            // In a real implementation, we would check OneDrive's status API or icon overlay
            return OneDriveSyncStatus.UpToDate;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine sync status for user {Sid}", userSid);
            return OneDriveSyncStatus.Unknown;
        }
    }

    private async Task<OneDriveSyncStatus> CheckFolderSyncStatusAsync(string folderPath, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Check for .lock files indicating sync in progress
                var lockFiles = Directory.GetFiles(folderPath, "*.lock", SearchOption.AllDirectories);
                if (lockFiles.Length > 0)
                {
                    return OneDriveSyncStatus.Syncing;
                }

                // Check for error files
                var errorFiles = Directory.GetFiles(folderPath, "*-error*", SearchOption.TopDirectoryOnly);
                if (errorFiles.Length > 0)
                {
                    return OneDriveSyncStatus.Error;
                }

                // Additional checks would go here in a real implementation
                return OneDriveSyncStatus.Unknown;
            }
            catch
            {
                return OneDriveSyncStatus.Unknown;
            }
        }, cancellationToken);
    }

    private async Task<bool> CheckForAuthenticationIssuesAsync(string userSid, OneDriveAccountInfo account)
    {
        try
        {
            // Check various indicators of authentication issues:

            // 1. OneDrive process not running but account configured
            if (!await _processDetector.IsOneDriveRunningForUserAsync(userSid) &&
                !string.IsNullOrEmpty(account.Email))
            {
                _logger.LogDebug("OneDrive not running for configured account, possible auth issue");
                return true;
            }

            // 2. Check for specific registry values that indicate auth problems
            // This would be expanded in a real implementation

            // 3. Check if sync folder exists but hasn't been modified recently
            if (!string.IsNullOrEmpty(account.UserFolder) && Directory.Exists(account.UserFolder))
            {
                var dirInfo = new DirectoryInfo(account.UserFolder);
                var daysSinceModified = (DateTime.UtcNow - dirInfo.LastWriteTimeUtc).TotalDays;

                if (daysSinceModified > 7)
                {
                    _logger.LogDebug("OneDrive folder hasn't been modified in {Days} days, possible sync issue",
                        daysSinceModified);
                    // This alone doesn't confirm auth issues, but it's a signal
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check for authentication issues");
            return false;
        }
    }

    private async Task<bool> IsFileSyncedAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Check file attributes for OneDrive placeholders
                var fileInfo = new FileInfo(filePath);
                var attributes = fileInfo.Attributes;

                // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS indicates a placeholder file
                const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

                // If it's not a placeholder, it's fully synced
                return (attributes & RecallOnDataAccess) == 0;
            }
            catch
            {
                // Assume synced if we can't check
                return true;
            }
        });
    }

    #endregion
}
