using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Interface for core detection logic for OneDrive status and configuration
/// </summary>
[SupportedOSPlatform("windows")]
public interface IOneDriveDetector
{
    /// <summary>
    /// Performs comprehensive OneDrive detection for a user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>OneDrive status information</returns>
    Task<OneDriveStatus> DetectOneDriveStatusAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects specific sync folder status
    /// </summary>
    /// <param name="folderPath">Path to the folder to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync progress information for the folder</returns>
    Task<SyncProgress> GetSyncProgressAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the sync status of a specific file
    /// </summary>
    /// <param name="filePath">Path to the file to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File sync status information</returns>
    Task<FileSyncStatus> GetFileSyncStatusAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets files that exist only locally and need to be uploaded
    /// </summary>
    /// <param name="folderPath">Path to the folder to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of files that need uploading</returns>
    Task<List<FileSyncStatus>> GetLocalOnlyFilesAsync(string folderPath, CancellationToken cancellationToken = default);
}
