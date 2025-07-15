using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Manages OneDrive selective sync configuration
/// </summary>
[SupportedOSPlatform("windows")]
public interface IOneDriveSyncController
{
    /// <summary>
    /// Adds a folder to OneDrive selective sync scope
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="accountId">OneDrive account ID</param>
    /// <param name="folderPath">Local folder path to add</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully added, false otherwise</returns>
    Task<bool> AddFolderToSyncScopeAsync(string userSid, string accountId, string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a folder from OneDrive selective sync scope
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="accountId">OneDrive account ID</param>
    /// <param name="folderPath">Local folder path to remove</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully removed, false otherwise</returns>
    Task<bool> RemoveFolderFromSyncScopeAsync(string userSid, string accountId, string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a folder is within OneDrive sync scope
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="folderPath">Local folder path to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if folder is in sync scope, false otherwise</returns>
    Task<bool> IsFolderInSyncScopeAsync(string userSid, string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all folders excluded from selective sync
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="accountId">OneDrive account ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of excluded folder paths</returns>
    Task<List<string>> GetExcludedFoldersAsync(string userSid, string accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures critical folders are included in sync scope
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="accountId">OneDrive account ID</param>
    /// <param name="criticalFolders">List of critical folder paths</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dictionary of folder paths and their inclusion status</returns>
    Task<Dictionary<string, bool>> EnsureCriticalFoldersIncludedAsync(
        string userSid, 
        string accountId, 
        List<string> criticalFolders, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets selective sync to include all folders
    /// </summary>
    /// <param name="userSid">User security identifier</param>
    /// <param name="accountId">OneDrive account ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if successfully reset, false otherwise</returns>
    Task<bool> ResetSelectiveSyncAsync(string userSid, string accountId, CancellationToken cancellationToken = default);
}