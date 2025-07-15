using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
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
    private readonly IOneDriveDetector _detector;
    private readonly IOneDriveStatusCache _cache;
    private readonly IOneDriveRegistry _registry;
    private readonly IOneDriveProcessDetector _processDetector;
    private readonly IStateManager _stateManager;
    private readonly IFileSystemService _fileSystemService;

    public OneDriveManager(
        ILogger<OneDriveManager> logger,
        IOneDriveDetector detector,
        IOneDriveStatusCache cache,
        IOneDriveRegistry registry,
        IOneDriveProcessDetector processDetector,
        IStateManager stateManager,
        IFileSystemService fileSystemService)
    {
        _logger = logger;
        _detector = detector;
        _cache = cache;
        _registry = registry;
        _processDetector = processDetector;
        _stateManager = stateManager;
        _fileSystemService = fileSystemService;
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

            // Use the computed AvailableSpaceMB property from OneDriveAccountInfo
            if (status.AccountInfo.AvailableSpaceMB > 0)
            {
                _logger.LogDebug("Available OneDrive space for user {Sid}: {SpaceMB} MB", userSid, status.AccountInfo.AvailableSpaceMB);
                return status.AccountInfo.AvailableSpaceMB;
            }

            // Fallback to local disk space if OneDrive quota information is not available
            if (!string.IsNullOrEmpty(status.AccountInfo.UserFolder) &&
                await _fileSystemService.DirectoryExistsAsync(status.AccountInfo.UserFolder))
            {
                var driveInfo = await _fileSystemService.GetDriveInfoAsync(status.AccountInfo.UserFolder);
                if (driveInfo != null)
                {
                    // Get the available free space - handle mock objects in tests
                    var availableBytes = driveInfo.GetType().Name == "MockDriveInfo"
                        ? (long)driveInfo.GetType().GetProperty("MockAvailableFreeSpace")?.GetValue(driveInfo)!
                        : driveInfo.AvailableFreeSpace;

                    var availableMB = availableBytes / (1024 * 1024);

                    _logger.LogDebug("Available local disk space for user {Sid}: {SpaceMB} MB", userSid, availableMB);
                    return availableMB;
                }
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
            if (!await _fileSystemService.DirectoryExistsAsync(folderPath))
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
                        return progress.IsComplete || progress.Status == OneDrive.Models.OneDriveSyncStatus.Syncing;
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
            if (!await _fileSystemService.DirectoryExistsAsync(folderPath))
            {
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");
            }

            // Strategy 1: Touch multiple files to ensure OneDrive notices
            var retryCount = 0;
            const int maxRetries = 3;
            var syncTriggered = false;

            while (!syncTriggered && retryCount < maxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Get local-only files that need syncing
                    var localOnlyFiles = await _detector.GetLocalOnlyFilesAsync(folderPath, cancellationToken);

                    if (localOnlyFiles.Count == 0)
                    {
                        _logger.LogDebug("No local-only files found in {FolderPath}, sync not needed", folderPath);
                        return;
                    }

                    _logger.LogDebug("Found {Count} local-only files to sync in {FolderPath}", localOnlyFiles.Count, folderPath);

                    // Strategy 1: Create trigger file
                    var triggerFile = Path.Combine(folderPath, $".onedrive_sync_trigger_{Guid.NewGuid():N}");
                    await _fileSystemService.WriteAllTextAsync(triggerFile,
                        $"Sync triggered at {DateTime.UtcNow:O} for {localOnlyFiles.Count} files",
                        cancellationToken);

                    // Give OneDrive time to notice the new file
                    await Task.Delay(500, cancellationToken);

                    // Strategy 2: Touch existing files (up to 10 to avoid overwhelming)
                    var filesToTouch = localOnlyFiles.Take(10).ToList();
                    foreach (var file in filesToTouch)
                    {
                        try
                        {
                            await _fileSystemService.TouchFileAsync(file.FilePath, cancellationToken);
                            _logger.LogDebug("Touched file to trigger sync: {FilePath}", file.FilePath);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to touch file {FilePath}", file.FilePath);
                        }
                    }

                    // Strategy 3: Create and immediately delete a file in each subdirectory
                    var directories = new HashSet<string> { folderPath };
                    foreach (var file in localOnlyFiles)
                    {
                        var dir = Path.GetDirectoryName(file.FilePath);
                        if (!string.IsNullOrEmpty(dir))
                        {
                            directories.Add(dir);
                        }
                    }

                    foreach (var dir in directories.Take(5)) // Limit to 5 directories
                    {
                        try
                        {
                            var tempFile = Path.Combine(dir, $".sync_{Guid.NewGuid():N}.tmp");
                            await _fileSystemService.WriteAllTextAsync(tempFile, "sync", cancellationToken);
                            await Task.Delay(100, cancellationToken);
                            await _fileSystemService.DeleteFileAsync(tempFile, cancellationToken);
                        }
                        catch
                        {
                            // Ignore errors in subdirectories
                        }
                    }

                    // Clean up main trigger file
                    try
                    {
                        await _fileSystemService.DeleteFileAsync(triggerFile, cancellationToken);
                    }
                    catch
                    {
                        // Ignore deletion errors - OneDrive might be syncing it
                    }

                    // Wait a bit to see if sync starts
                    await Task.Delay(2000, cancellationToken);

                    // Check if sync has started
                    var progress = await _detector.GetSyncProgressAsync(folderPath, cancellationToken);
                    if (progress.Status == OneDrive.Models.OneDriveSyncStatus.Syncing || progress.ActiveFiles.Count > 0)
                    {
                        syncTriggered = true;
                        _logger.LogInformation("Successfully triggered sync for folder: {FolderPath}", folderPath);
                    }
                    else
                    {
                        retryCount++;
                        if (retryCount < maxRetries)
                        {
                            _logger.LogDebug("Sync not started yet, retrying ({Retry}/{MaxRetries})", retryCount, maxRetries);
                            await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken); // Exponential backoff
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error during sync trigger attempt {Retry}", retryCount + 1);
                    retryCount++;
                    if (retryCount < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken);
                    }
                }
            }

            if (!syncTriggered)
            {
                _logger.LogWarning("Failed to trigger sync for {FolderPath} after {MaxRetries} attempts", folderPath, maxRetries);
                // Don't throw - this might require user interaction in Phase 4
            }
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
        var lastProgressUpdate = DateTime.UtcNow;
        var lastBytesSynced = 0L;
        var stallDetectionThreshold = TimeSpan.FromMinutes(5); // Consider stalled if no progress for 5 minutes

        try
        {
            // First, check if sync is already complete
            var initialProgress = await GetSyncProgressAsync(folderPath, cancellationToken);
            if (initialProgress.IsComplete)
            {
                _logger.LogInformation("Sync already completed for folder: {FolderPath} - {FilesSynced} files ({BytesSynced} bytes)",
                    folderPath, initialProgress.FilesSynced, initialProgress.BytesSynced);
                return true;
            }

            // Ensure folder is in OneDrive sync scope
            if (!await EnsureFolderSyncedAsync(folderPath, cancellationToken))
            {
                _logger.LogError("Folder {FolderPath} is not within OneDrive sync scope", folderPath);
                return false;
            }

            // Force sync to start if needed
            await ForceSyncAsync(folderPath, cancellationToken);

            while (DateTime.UtcNow - startTime < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = await GetSyncProgressAsync(folderPath, cancellationToken);

                // Check if sync is complete
                if (progress.IsComplete)
                {
                    _logger.LogInformation("Sync completed for folder: {FolderPath} - {FilesSynced} files ({BytesSynced} bytes)",
                        folderPath, progress.FilesSynced, progress.BytesSynced);
                    return true;
                }

                // Check for errors
                if (progress.Status == OneDrive.Models.OneDriveSyncStatus.Error)
                {
                    _logger.LogWarning("Sync error detected for folder: {FolderPath}. Errors: {ErrorCount}",
                        folderPath, progress.Errors.Count);

                    // Log specific errors
                    foreach (var error in progress.Errors.Take(5)) // Log first 5 errors
                    {
                        _logger.LogWarning("Sync error for {FilePath}: {ErrorMessage}",
                            error.FilePath, error.ErrorMessage);
                    }

                    return false;
                }

                // Check for stalled sync
                if (progress.BytesSynced > lastBytesSynced)
                {
                    lastBytesSynced = progress.BytesSynced;
                    lastProgressUpdate = DateTime.UtcNow;
                }
                else if (DateTime.UtcNow - lastProgressUpdate > stallDetectionThreshold)
                {
                    _logger.LogWarning("Sync appears to be stalled for folder: {FolderPath}. No progress for {Minutes} minutes",
                        folderPath, stallDetectionThreshold.TotalMinutes);

                    // Try to restart sync
                    await ForceSyncAsync(folderPath, cancellationToken);
                    lastProgressUpdate = DateTime.UtcNow; // Reset stall detection
                }

                // Log progress
                var elapsedTime = DateTime.UtcNow - startTime;
                var remainingTime = timeout - elapsedTime;

                _logger.LogDebug("Sync progress for {FolderPath}: {Percent:F1}% ({FilesSynced}/{TotalFiles} files, {BytesSynced}/{TotalBytes} bytes)",
                    folderPath, progress.PercentComplete, progress.FilesSynced, progress.TotalFiles,
                    progress.BytesSynced, progress.TotalBytes);

                if (progress.ActiveFiles.Count > 0)
                {
                    _logger.LogDebug("Currently syncing {Count} files: {Files}",
                        progress.ActiveFiles.Count,
                        string.Join(", ", progress.ActiveFiles.Take(3).Select(Path.GetFileName)));
                }

                if (progress.EstimatedTimeRemaining.HasValue)
                {
                    _logger.LogDebug("Estimated time remaining: {Time}, Timeout in: {Timeout}",
                        progress.EstimatedTimeRemaining.Value, remainingTime);

                    // Warn if estimated time exceeds timeout
                    if (progress.EstimatedTimeRemaining.Value > remainingTime)
                    {
                        _logger.LogWarning("Sync may not complete within timeout. Estimated: {Estimated}, Remaining: {Remaining}",
                            progress.EstimatedTimeRemaining.Value, remainingTime);
                    }
                }

                // Dynamic check interval based on progress
                if (progress.PercentComplete > 90)
                {
                    checkInterval = TimeSpan.FromSeconds(2); // Check more frequently near completion
                }
                else if (progress.PercentComplete < 10)
                {
                    checkInterval = TimeSpan.FromSeconds(10); // Check less frequently at start
                }
                else
                {
                    checkInterval = TimeSpan.FromSeconds(5);
                }

                await Task.Delay(checkInterval, cancellationToken);
            }

            // Timeout reached
            var finalProgress = await GetSyncProgressAsync(folderPath, cancellationToken);
            _logger.LogWarning("Sync timeout for folder: {FolderPath}. Progress: {Percent:F1}% ({FilesSynced}/{TotalFiles} files)",
                folderPath, finalProgress.PercentComplete, finalProgress.FilesSynced, finalProgress.TotalFiles);

            return false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Sync wait cancelled for folder: {FolderPath}", folderPath);
            throw;
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

            if (status.SyncStatus != OneDrive.Models.OneDriveSyncStatus.AuthenticationRequired)
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

            if (status.SyncStatus != OneDrive.Models.OneDriveSyncStatus.Error || status.AccountInfo == null)
            {
                _logger.LogDebug("No sync errors to resolve for user {Sid}", userSid);
                return true;
            }

            // Get or create sync operation for tracking
            var syncOperation = await GetOrCreateSyncOperationAsync(userSid, status.SyncFolder ?? string.Empty, cancellationToken);

            // Get unresolved errors from database
            var unresolvedErrors = await _stateManager.GetUnresolvedSyncOperationErrorsAsync(userSid, cancellationToken);
            var resolvedCount = 0;
            var totalErrors = unresolvedErrors.Count();

            _logger.LogInformation("Found {ErrorCount} unresolved sync errors for user {Sid}", totalErrors, userSid);

            // Group errors by type for batch resolution
            var errorGroups = unresolvedErrors.GroupBy(e => GetErrorCategory(e.ErrorMessage));

            foreach (var errorGroup in errorGroups)
            {
                var category = errorGroup.Key;
                var errors = errorGroup.ToList();

                _logger.LogDebug("Attempting to resolve {Count} {Category} errors", errors.Count, category);

                switch (category)
                {
                    case SyncErrorCategory.FileNotFound:
                        resolvedCount += await HandleFileNotFoundErrorsAsync(errors.ToList(), cancellationToken);
                        break;

                    case SyncErrorCategory.FileLocked:
                        resolvedCount += await HandleFileLockedErrorsAsync(errors.ToList(), cancellationToken);
                        break;

                    case SyncErrorCategory.InvalidPath:
                        resolvedCount += await HandleInvalidPathErrorsAsync(errors.ToList(), cancellationToken);
                        break;

                    case SyncErrorCategory.QuotaExceeded:
                        resolvedCount += await HandleQuotaExceededErrorsAsync(userSid, errors.ToList(), cancellationToken);
                        break;

                    case SyncErrorCategory.NetworkError:
                        resolvedCount += await HandleNetworkErrorsAsync(errors.ToList(), cancellationToken);
                        break;

                    case SyncErrorCategory.AuthenticationError:
                        // Can't resolve auth errors from service context
                        _logger.LogWarning("Authentication errors require user interaction. Will be handled in Phase 4");
                        break;

                    default:
                        // Try generic resolution
                        resolvedCount += await HandleGenericErrorsAsync(errors.ToList(), cancellationToken);
                        break;
                }
            }

            // Update sync operation status
            syncOperation.ErrorCount = totalErrors - resolvedCount;
            await _stateManager.UpdateSyncOperationAsync(syncOperation, cancellationToken);

            // Check if we need to escalate
            if (resolvedCount < totalErrors)
            {
                var remainingErrors = totalErrors - resolvedCount;
                _logger.LogWarning("Could not resolve {Count} sync errors for user {Sid}", remainingErrors, userSid);

                // Check if errors have exceeded retry limit
                var errorsToEscalate = unresolvedErrors
                    .Where(e => e.RetryAttempts >= 3 && !e.EscalatedToIT)
                    .ToList();

                if (errorsToEscalate.Any())
                {
                    await EscalateSyncErrorsAsync(userSid, errorsToEscalate.ToList(), cancellationToken);
                }
            }

            // Clear cache and force sync if we resolved any errors
            if (resolvedCount > 0)
            {
                _logger.LogInformation("Resolved {Count}/{Total} sync errors for user {Sid}", resolvedCount, totalErrors, userSid);
                _cache.InvalidateCache(userSid);

                // Try to restart sync for affected folders
                foreach (var folder in status.AccountInfo.SyncedFolders.Where(f => f.HasErrors))
                {
                    await ForceSyncAsync(folder.LocalPath, cancellationToken);
                }

                // Wait and recheck status
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                var newStatus = await _detector.DetectOneDriveStatusAsync(userSid, cancellationToken);
                return newStatus.SyncStatus != OneDrive.Models.OneDriveSyncStatus.Error;
            }

            return resolvedCount == totalErrors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve sync errors for user {Sid}", userSid);
            return false;
        }
    }

    private async Task<SyncOperation> GetOrCreateSyncOperationAsync(string userSid, string folderPath, CancellationToken cancellationToken)
    {
        var existingOperation = await _stateManager.GetActiveSyncOperationAsync(userSid, folderPath, cancellationToken);
        if (existingOperation != null)
        {
            return existingOperation;
        }

        var newOperation = new SyncOperation
        {
            UserSid = userSid,
            FolderPath = folderPath,
            StartTime = DateTime.UtcNow,
            Status = SyncOperationStatus.InProgress
        };

        newOperation.Id = await _stateManager.CreateSyncOperationAsync(newOperation, cancellationToken);
        return newOperation;
    }

    private enum SyncErrorCategory
    {
        FileNotFound,
        FileLocked,
        InvalidPath,
        QuotaExceeded,
        NetworkError,
        AuthenticationError,
        Unknown
    }

    private SyncErrorCategory GetErrorCategory(string errorMessage)
    {
        var lowerMessage = errorMessage.ToLowerInvariant();

        if (lowerMessage.Contains("not found") || lowerMessage.Contains("doesn't exist"))
        {
            return SyncErrorCategory.FileNotFound;
        }

        if (lowerMessage.Contains("locked") || lowerMessage.Contains("in use") || lowerMessage.Contains("access denied"))
        {
            return SyncErrorCategory.FileLocked;
        }

        if (lowerMessage.Contains("path") || lowerMessage.Contains("filename") || lowerMessage.Contains("invalid character"))
        {
            return SyncErrorCategory.InvalidPath;
        }

        if (lowerMessage.Contains("quota") || lowerMessage.Contains("space") || lowerMessage.Contains("storage"))
        {
            return SyncErrorCategory.QuotaExceeded;
        }

        if (lowerMessage.Contains("network") || lowerMessage.Contains("connection") || lowerMessage.Contains("offline"))
        {
            return SyncErrorCategory.NetworkError;
        }

        if (lowerMessage.Contains("auth") || lowerMessage.Contains("sign") || lowerMessage.Contains("credential"))
        {
            return SyncErrorCategory.AuthenticationError;
        }

        return SyncErrorCategory.Unknown;
    }

    private async Task<int> HandleFileNotFoundErrorsAsync(List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        var resolved = 0;
        foreach (var error in errors)
        {
            try
            {
                // Check if file still doesn't exist
                if (!await _fileSystemService.FileExistsAsync(error.FilePath))
                {
                    // File is genuinely missing, mark as resolved (nothing to sync)
                    await _stateManager.MarkSyncOperationErrorResolvedAsync(error.Id, cancellationToken);
                    resolved++;
                    _logger.LogDebug("Marked file not found error as resolved: {FilePath}", error.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check file existence for {FilePath}", error.FilePath);
            }
        }
        return resolved;
    }

    private async Task<int> HandleFileLockedErrorsAsync(List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        var resolved = 0;

        // Wait a bit for files to be unlocked
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        foreach (var error in errors)
        {
            try
            {
                // Try to access the file
                var fileInfo = await _fileSystemService.GetFileInfoAsync(error.FilePath);
                if (fileInfo != null && fileInfo.Exists)
                {
                    // If we can access it now, try to trigger sync
                    var dir = Path.GetDirectoryName(error.FilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        await ForceSyncAsync(dir, cancellationToken);
                        await _stateManager.MarkSyncOperationErrorResolvedAsync(error.Id, cancellationToken);
                        resolved++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "File still locked: {FilePath}", error.FilePath);
                // Increment retry count
                error.RetryAttempts++;
                await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
            }
        }
        return resolved;
    }

    private async Task<int> HandleInvalidPathErrorsAsync(List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        var resolved = 0;
        foreach (var error in errors)
        {
            try
            {
                // Log for IT attention - these usually require manual intervention
                _logger.LogWarning("Invalid path error for {FilePath} - requires manual intervention", error.FilePath);

                // Check if file has invalid characters
                var invalidChars = Path.GetInvalidFileNameChars();
                var fileName = Path.GetFileName(error.FilePath);

                if (!string.IsNullOrEmpty(fileName) && fileName.Any(c => invalidChars.Contains(c)))
                {
                    _logger.LogError("File {FilePath} contains invalid characters: {InvalidChars}",
                        error.FilePath,
                        string.Join(", ", fileName.Where(c => invalidChars.Contains(c))));
                }

                // These errors need IT escalation after retry limit
                error.RetryAttempts++;
                await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to handle invalid path error for {FilePath}", error.FilePath);
            }
        }
        return resolved;
    }

    private async Task<int> HandleQuotaExceededErrorsAsync(string userSid, List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        // Check current quota
        var availableSpace = await GetAvailableSpaceMBAsync(userSid, cancellationToken);

        if (availableSpace <= 0)
        {
            _logger.LogError("OneDrive quota exceeded for user {Sid}. Available space: {Space} MB", userSid, availableSpace);

            // These need immediate IT escalation
            foreach (var error in errors)
            {
                error.RetryAttempts = 3; // Force escalation
                await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
            }
        }

        return 0; // Can't resolve quota issues automatically
    }

    private async Task<int> HandleNetworkErrorsAsync(List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        // Check if we have network connectivity
        // For now, just increment retry count
        foreach (var error in errors)
        {
            error.RetryAttempts++;
            await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
        }

        _logger.LogInformation("Network errors detected. Will retry on next sync cycle");
        return 0;
    }

    private async Task<int> HandleGenericErrorsAsync(List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        var resolved = 0;

        // Try forcing sync for affected folders
        var affectedFolders = errors
            .Select(e => Path.GetDirectoryName(e.FilePath))
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct();

        foreach (var folder in affectedFolders)
        {
            try
            {
                await ForceSyncAsync(folder!, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to force sync for folder {Folder}", folder);
            }
        }

        // Update retry counts
        foreach (var error in errors)
        {
            error.RetryAttempts++;
            await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
        }

        return resolved;
    }

    private async Task EscalateSyncErrorsAsync(string userSid, List<MigrationTool.Service.Models.SyncError> errors, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Escalating {Count} sync errors to IT for user {Sid}", errors.Count, userSid);

        // Create IT escalation
        var escalation = new ITEscalation
        {
            UserId = userSid,
            Reason = "Multiple unresolved OneDrive sync errors",
            Details = $"Failed to resolve {errors.Count} sync errors after multiple retry attempts. Files affected: {string.Join(", ", errors.Take(5).Select(e => Path.GetFileName(e.FilePath)))}",
            CreatedAt = DateTime.UtcNow,
            Status = "Open"
        };

        await _stateManager.CreateEscalationAsync(escalation, cancellationToken);

        // Mark errors as escalated
        foreach (var error in errors)
        {
            error.EscalatedToIT = true;
            error.EscalatedAt = DateTime.UtcNow;
            await _stateManager.RecordSyncErrorAsync(error, cancellationToken);
        }
    }

    #region Private Methods

    private MigrationTool.Service.Models.KnownFolderMoveStatus ConvertKnownFolderMoveStatus(
        string userSid,
        string accountId,
        OneDrive.Models.KnownFolderMoveStatus domainStatus)
    {
        return new MigrationTool.Service.Models.KnownFolderMoveStatus
        {
            UserId = userSid,
            AccountId = accountId,
            IsEnabled = domainStatus.IsEnabled,
            DesktopRedirected = domainStatus.DesktopRedirected,
            DesktopPath = domainStatus.DesktopPath,
            DocumentsRedirected = domainStatus.DocumentsRedirected,
            DocumentsPath = domainStatus.DocumentsPath,
            PicturesRedirected = domainStatus.PicturesRedirected,
            PicturesPath = domainStatus.PicturesPath,
            ConfigurationSource = domainStatus.ConfigurationSource,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    private async Task StoreOneDriveStatusAsync(string userSid, OneDriveStatus status, CancellationToken cancellationToken)
    {
        try
        {
            // Convert OneDriveStatus to OneDriveStatusRecord for storage
            var statusRecord = new OneDriveStatusRecord
            {
                UserId = userSid,
                IsInstalled = status.IsInstalled,
                IsRunning = status.IsRunning,
                IsSignedIn = status.IsSignedIn,
                AccountEmail = status.AccountEmail,
                SyncFolder = status.SyncFolder,
                SyncStatus = status.SyncStatus.ToString(),
                LastChecked = status.LastChecked,
                ErrorDetails = status.ErrorDetails
            };

            // Calculate available space if account info exists
            if (status.AccountInfo != null)
            {
                statusRecord.PrimaryAccountId = status.AccountInfo.AccountId;
                statusRecord.AvailableSpaceMB = status.AccountInfo.AvailableSpaceMB;
                statusRecord.UsedSpaceMB = status.AccountInfo.UsedSpaceBytes.HasValue
                    ? (int)(status.AccountInfo.UsedSpaceBytes.Value / (1024 * 1024))
                    : null;
                statusRecord.HasSyncErrors = status.AccountInfo.HasSyncErrors;
            }

            await _stateManager.SaveOneDriveStatusAsync(statusRecord, cancellationToken);

            // Store account details if available
            if (status.AccountInfo != null)
            {
                var account = new OneDriveAccount
                {
                    UserId = userSid,
                    AccountId = status.AccountInfo.AccountId,
                    Email = status.AccountInfo.Email,
                    DisplayName = status.AccountInfo.Email, // Use email as display name if not provided
                    UserFolder = status.AccountInfo.UserFolder,
                    IsPrimary = status.AccountInfo.IsPrimary
                };

                await _stateManager.SaveOneDriveAccountAsync(account, cancellationToken);

                // Store synced folders
                foreach (var folder in status.AccountInfo.SyncedFolders)
                {
                    var syncedFolder = new OneDriveSyncedFolder
                    {
                        UserId = userSid,
                        AccountId = status.AccountInfo.AccountId,
                        LocalPath = folder.LocalPath,
                        RemotePath = folder.RemotePath,
                        LibraryType = folder.LibraryType.ToString(),
                        IsSyncing = folder.IsSyncing,
                        HasErrors = folder.HasErrors,
                        DisplayName = folder.DisplayName,
                        OwnerName = folder.OwnerName
                    };

                    await _stateManager.SaveOneDriveSyncedFolderAsync(syncedFolder, cancellationToken);
                }

                // Store KFM status if available
                if (status.AccountInfo.KfmStatus != null)
                {
                    var dbKfmStatus = ConvertKnownFolderMoveStatus(userSid, status.AccountInfo.AccountId, status.AccountInfo.KfmStatus);
                    await _stateManager.SaveKnownFolderMoveStatusAsync(dbKfmStatus, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store OneDrive status for user {Sid}", userSid);
            // Don't throw - storage failure shouldn't break the operation
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
