using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Manages OneDrive detection, status monitoring, and sync operations
/// </summary>
public interface IOneDriveManager
{
    /// <summary>
    /// Gets the OneDrive status for a specific user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>OneDrive status information</returns>
    Task<OneDriveStatus> GetStatusAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the available space in megabytes for a user's OneDrive
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Available space in MB, or -1 if unable to determine</returns>
    Task<long> GetAvailableSpaceMBAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a specific folder is configured for OneDrive sync
    /// </summary>
    /// <param name="folderPath">The folder path to sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the folder is synced or was successfully configured for sync</returns>
    Task<bool> EnsureFolderSyncedAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the sync progress for a specific folder
    /// </summary>
    /// <param name="folderPath">The folder path to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync progress information</returns>
    Task<SyncProgress> GetSyncProgressAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Forces a sync operation for a specific folder
    /// </summary>
    /// <param name="folderPath">The folder path to sync</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ForceSyncAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Waits for a folder to complete syncing
    /// </summary>
    /// <param name="folderPath">The folder path to wait for</param>
    /// <param name="timeout">Maximum time to wait</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if sync completed within timeout</returns>
    Task<bool> WaitForSyncAsync(string folderPath, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to recover from authentication errors
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if recovery was successful</returns>
    Task<bool> TryRecoverAuthenticationAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to resolve sync errors for a user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if errors were resolved</returns>
    Task<bool> TryResolveSyncErrorsAsync(string userSid, CancellationToken cancellationToken = default);
}
