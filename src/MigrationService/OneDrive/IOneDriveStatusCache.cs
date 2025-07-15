using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Interface for caching OneDrive status to improve performance
/// </summary>
public interface IOneDriveStatusCache
{
    /// <summary>
    /// Gets a cached status if available and not expired
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <returns>Cached OneDrive status or null if not cached or expired</returns>
    OneDriveStatus? GetCachedStatus(string userSid);

    /// <summary>
    /// Caches a status for a user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="status">OneDrive status to cache</param>
    void CacheStatus(string userSid, OneDriveStatus status);

    /// <summary>
    /// Invalidates cached status for a user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    void InvalidateCache(string userSid);

    /// <summary>
    /// Clears all cached entries
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Gets the number of cached entries
    /// </summary>
    int CacheSize { get; }

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    /// <returns>Cache statistics including valid/expired entries</returns>
    CacheStatistics GetStatistics();
}
