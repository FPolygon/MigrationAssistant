using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Main implementation of OneDrive management functionality
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveManager : IOneDriveManager
{
    private readonly ILogger<OneDriveManager> _logger;
    private readonly OneDriveDetector _detector;
    private readonly OneDriveStatusCache _cache;
    private readonly IOneDriveRegistry _registry;
    private readonly OneDriveProcessDetector _processDetector;
    private readonly IStateManager _stateManager;

    public OneDriveManager(
        ILogger<OneDriveManager> logger,
        OneDriveDetector detector,
        OneDriveStatusCache cache,
        IOneDriveRegistry registry,
        OneDriveProcessDetector processDetector,
        IStateManager stateManager)
    {
        _logger = logger;
        _detector = detector;
        _cache = cache;
        _registry = registry;
        _processDetector = processDetector;
        _stateManager = stateManager;
    }

    /// <inheritdoc/>
    public async Task<OneDriveStatus> GetStatusAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting OneDrive status for user {Sid}", userSid);

        try
        {
            // Check cache first
            var cachedStatus = _cache.GetCachedStatus(userSid);
            if (cachedStatus != null)
            {
                return cachedStatus;
            }

            // Perform full detection
            var status = await _detector.DetectOneDriveStatusAsync(userSid, cancellationToken);

            // Cache the result
            _cache.CacheStatus(userSid, status);

            // Store in database for persistence
            await StoreOneDriveStatusAsync(userSid, status, cancellationToken);

            return status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive status for user {Sid}", userSid);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetAvailableSpaceMBAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting available OneDrive space for user {Sid}", userSid);

        try
        {
            var status = await GetStatusAsync(userSid, cancellationToken);

            if (!status.IsSignedIn || status.AccountInfo == null)
            {
                _logger.LogWarning("User {Sid} is not signed into OneDrive", userSid);
                return -1;
            }

            // In a real implementation, we would query OneDrive API or use COM interfaces
            // For now, we'll calculate based on local disk space as a placeholder
            if (!string.IsNullOrEmpty(status.AccountInfo.UserFolder) &&
                Directory.Exists(status.AccountInfo.UserFolder))
            {
                var driveInfo = new DriveInfo(Path.GetPathRoot(status.AccountInfo.UserFolder)!);
                var availableBytes = driveInfo.AvailableFreeSpace;
                var availableMB = availableBytes / (1024 * 1024);

                _logger.LogDebug("Available space for user {Sid}: {SpaceMB} MB", userSid, availableMB);
                return availableMB;
            }

            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get available space for user {Sid}", userSid);
            return -1;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> EnsureFolderSyncedAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Ensuring folder is synced: {FolderPath}", folderPath);

        try
        {
            if (!Directory.Exists(folderPath))
            {
                _logger.LogWarning("Folder does not exist: {FolderPath}", folderPath);
                return false;
            }

            // Check if folder is already within a OneDrive sync folder
            var allUsers = await _stateManager.GetUserProfilesAsync(cancellationToken);

            foreach (var user in allUsers)
            {
                var syncedFolders = await _registry.GetSyncedFoldersAsync(user.UserId);

                foreach (var syncFolder in syncedFolders)
                {
                    if (folderPath.StartsWith(syncFolder.LocalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug("Folder {FolderPath} is already within synced folder {SyncFolder}",
                            folderPath, syncFolder.LocalPath);

                        // Check if it's actually syncing
                        var progress = await _detector.GetSyncProgressAsync(folderPath, cancellationToken);
                        return progress.IsComplete || progress.Status == OneDriveSyncStatus.Syncing;
                    }
                }
            }

            _logger.LogWarning("Folder {FolderPath} is not within any OneDrive sync folder", folderPath);
            // In Phase 4, we would send a message to the agent to prompt the user to add this folder
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure folder sync for {FolderPath}", folderPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<SyncProgress> GetSyncProgressAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting sync progress for folder: {FolderPath}", folderPath);

        try
        {
            return await _detector.GetSyncProgressAsync(folderPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sync progress for {FolderPath}", folderPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ForceSyncAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Forcing sync for folder: {FolderPath}", folderPath);

        try
        {
            if (!Directory.Exists(folderPath))
            {
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
            }

            // Touch a file in the folder to trigger sync
            var triggerFile = Path.Combine(folderPath, ".onedrive_sync_trigger");
            await File.WriteAllTextAsync(triggerFile, DateTime.UtcNow.ToString("O"), cancellationToken);
            await Task.Delay(100, cancellationToken);

            try
            {
                File.Delete(triggerFile);
            }
            catch
            {
                // Ignore deletion errors
            }

            _logger.LogDebug("Triggered sync for folder: {FolderPath}", folderPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to force sync for {FolderPath}", folderPath);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> WaitForSyncAsync(string folderPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Waiting for sync to complete for folder: {FolderPath} (timeout: {Timeout})",
            folderPath, timeout);

        var startTime = DateTime.UtcNow;
        var checkInterval = TimeSpan.FromSeconds(5);

        try
        {
            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = await GetSyncProgressAsync(folderPath, cancellationToken);

                if (progress.IsComplete)
                {
                    _logger.LogInformation("Sync completed for folder: {FolderPath}", folderPath);
                    return true;
                }

                if (progress.Status == OneDriveSyncStatus.Error)
                {
                    _logger.LogWarning("Sync error detected for folder: {FolderPath}", folderPath);
                    return false;
                }

                _logger.LogDebug("Sync progress for {FolderPath}: {Percent}% ({FilesSynced}/{TotalFiles} files)",
                    folderPath, progress.PercentComplete, progress.FilesSynced, progress.TotalFiles);

                await Task.Delay(checkInterval, cancellationToken);
            }

            _logger.LogWarning("Sync timeout for folder: {FolderPath}", folderPath);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for sync for {FolderPath}", folderPath);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryRecoverAuthenticationAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to recover OneDrive authentication for user {Sid}", userSid);

        try
        {
            var status = await GetStatusAsync(userSid, cancellationToken);

            if (status.SyncStatus != OneDriveSyncStatus.AuthenticationRequired)
            {
                _logger.LogDebug("No authentication recovery needed for user {Sid}", userSid);
                return true;
            }

            // In Phase 4, we would:
            // 1. Send a message to the agent to prompt user to sign in
            // 2. Try to launch OneDrive with /signin parameter
            // 3. Monitor for successful authentication

            _logger.LogWarning("Authentication recovery requires user interaction. " +
                "This will be implemented in Phase 4 with the agent.");

            // For now, we'll log the issue for IT escalation
            await LogAuthenticationIssueAsync(userSid, status, cancellationToken);

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recover authentication for user {Sid}", userSid);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryResolveSyncErrorsAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Attempting to resolve sync errors for user {Sid}", userSid);

        try
        {
            var status = await GetStatusAsync(userSid, cancellationToken);

            if (status.SyncStatus != OneDriveSyncStatus.Error || status.AccountInfo == null)
            {
                _logger.LogDebug("No sync errors to resolve for user {Sid}", userSid);
                return true;
            }

            var resolvedAny = false;

            // Attempt various recovery strategies

            // 1. Check if OneDrive process is running
            if (!status.IsRunning)
            {
                _logger.LogInformation("OneDrive not running for user {Sid}, cannot start from service context", userSid);
                // In Phase 4, agent would attempt to start OneDrive
            }

            // 2. Check for specific error patterns in synced folders
            foreach (var folder in status.AccountInfo.SyncedFolders)
            {
                if (folder.HasErrors)
                {
                    _logger.LogDebug("Checking for errors in folder: {FolderPath}", folder.LocalPath);

                    // Try to trigger a resync
                    await ForceSyncAsync(folder.LocalPath, cancellationToken);
                    resolvedAny = true;
                }
            }

            // 3. Clear the cache to force fresh detection on next check
            _cache.InvalidateCache(userSid);

            if (resolvedAny)
            {
                _logger.LogInformation("Attempted sync error resolution for user {Sid}", userSid);

                // Wait a bit and recheck status
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                var newStatus = await _detector.DetectOneDriveStatusAsync(userSid, cancellationToken);

                return newStatus.SyncStatus != OneDriveSyncStatus.Error;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve sync errors for user {Sid}", userSid);
            return false;
        }
    }

    #region Private Methods

    private async Task StoreOneDriveStatusAsync(string userSid, OneDriveStatus status, CancellationToken cancellationToken)
    {
        try
        {
            // This will be implemented when we update IStateManager in task 10
            _logger.LogDebug("OneDrive status storage will be implemented with IStateManager update");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store OneDrive status for user {Sid}", userSid);
            // Don't throw - caching failure shouldn't break the operation
        }
    }

    private async Task LogAuthenticationIssueAsync(string userSid, OneDriveStatus status, CancellationToken cancellationToken)
    {
        try
        {
            // Log for IT escalation
            _logger.LogWarning("OneDrive authentication required for user {Sid}. Account: {Email}",
                userSid, status.AccountEmail);

            // In full implementation, this would create an escalation record
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log authentication issue for user {Sid}", userSid);
        }
    }

    #endregion
}
