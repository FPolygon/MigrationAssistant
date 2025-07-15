using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Caches OneDrive status to improve performance
/// </summary>
public class OneDriveStatusCache
{
    private readonly ILogger<OneDriveStatusCache> _logger;
    private readonly ConcurrentDictionary<string, CachedStatus> _cache;
    private readonly TimeSpan _cacheExpiry;
    private readonly object _cleanupLock = new();
    private DateTime _lastCleanup = DateTime.UtcNow;

    public OneDriveStatusCache(ILogger<OneDriveStatusCache> logger, TimeSpan? cacheExpiry = null)
    {
        _logger = logger;
        _cache = new ConcurrentDictionary<string, CachedStatus>(StringComparer.OrdinalIgnoreCase);
        _cacheExpiry = cacheExpiry ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Gets a cached status if available and not expired
    /// </summary>
    public OneDriveStatus? GetCachedStatus(string userSid)
    {
        CleanupIfNeeded();

        if (_cache.TryGetValue(userSid, out var cached))
        {
            if (DateTime.UtcNow - cached.CachedAt < _cacheExpiry)
            {
                _logger.LogDebug("Returning cached OneDrive status for user {Sid}", userSid);
                return cached.Status;
            }
            else
            {
                _logger.LogDebug("Cache expired for user {Sid}", userSid);
                _cache.TryRemove(userSid, out _);
            }
        }

        return null;
    }

    /// <summary>
    /// Caches a status for a user
    /// </summary>
    public void CacheStatus(string userSid, OneDriveStatus status)
    {
        _logger.LogDebug("Caching OneDrive status for user {Sid}", userSid);

        _cache.AddOrUpdate(userSid,
            new CachedStatus { Status = status, CachedAt = DateTime.UtcNow },
            (key, existing) => new CachedStatus { Status = status, CachedAt = DateTime.UtcNow });
    }

    /// <summary>
    /// Invalidates cached status for a user
    /// </summary>
    public void InvalidateCache(string userSid)
    {
        _logger.LogDebug("Invalidating cache for user {Sid}", userSid);
        _cache.TryRemove(userSid, out _);
    }

    /// <summary>
    /// Clears all cached entries
    /// </summary>
    public void ClearCache()
    {
        _logger.LogInformation("Clearing OneDrive status cache");
        _cache.Clear();
    }

    /// <summary>
    /// Gets the number of cached entries
    /// </summary>
    public int CacheSize => _cache.Count;

    /// <summary>
    /// Gets cache statistics
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        var now = DateTime.UtcNow;
        var validEntries = 0;
        var expiredEntries = 0;
        var oldestEntry = DateTime.MaxValue;
        var newestEntry = DateTime.MinValue;

        foreach (var entry in _cache.Values)
        {
            if (now - entry.CachedAt < _cacheExpiry)
            {
                validEntries++;
            }
            else
            {
                expiredEntries++;
            }

            if (entry.CachedAt < oldestEntry)
            {
                oldestEntry = entry.CachedAt;
            }

            if (entry.CachedAt > newestEntry)
            {
                newestEntry = entry.CachedAt;
            }
        }

        return new CacheStatistics
        {
            TotalEntries = _cache.Count,
            ValidEntries = validEntries,
            ExpiredEntries = expiredEntries,
            OldestEntry = oldestEntry != DateTime.MaxValue ? oldestEntry : null,
            NewestEntry = newestEntry != DateTime.MinValue ? newestEntry : null,
            CacheExpiryMinutes = _cacheExpiry.TotalMinutes
        };
    }

    private void CleanupIfNeeded()
    {
        // Cleanup expired entries every 10 minutes
        if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromMinutes(10))
        {
            lock (_cleanupLock)
            {
                if (DateTime.UtcNow - _lastCleanup > TimeSpan.FromMinutes(10))
                {
                    _lastCleanup = DateTime.UtcNow;
                    Task.Run(() => CleanupExpiredEntries());
                }
            }
        }
    }

    private void CleanupExpiredEntries()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredKeys = new List<string>();

            foreach (var kvp in _cache)
            {
                if (now - kvp.Value.CachedAt >= _cacheExpiry)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            foreach (var key in expiredKeys)
            {
                _cache.TryRemove(key, out _);
            }

            if (expiredKeys.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired cache entries", expiredKeys.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during cache cleanup");
        }
    }

    private class CachedStatus
    {
        public OneDriveStatus Status { get; set; } = null!;
        public DateTime CachedAt { get; set; }
    }
}

/// <summary>
/// Cache statistics
/// </summary>
public class CacheStatistics
{
    public int TotalEntries { get; set; }
    public int ValidEntries { get; set; }
    public int ExpiredEntries { get; set; }
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
    public double CacheExpiryMinutes { get; set; }
}
