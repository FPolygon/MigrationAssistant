using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Manages manual classification overrides for user profiles
/// </summary>
[SupportedOSPlatform("windows")]
public class ClassificationOverrideManager : IClassificationOverrideManager
{
    private readonly ILogger<ClassificationOverrideManager> _logger;
    private readonly IStateManager _stateManager;
    private readonly Dictionary<string, ClassificationOverride> _overrideCache;
    private readonly object _cacheLock = new();
    private DateTime _lastCacheRefresh = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public ClassificationOverrideManager(
        ILogger<ClassificationOverrideManager> logger,
        IStateManager stateManager)
    {
        _logger = logger;
        _stateManager = stateManager;
        _overrideCache = new Dictionary<string, ClassificationOverride>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Applies a manual classification override
    /// </summary>
    public async Task<OverrideResult> ApplyOverrideAsync(
        string userId,
        ProfileClassification classification,
        string overrideBy,
        string reason,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Applying classification override for user {UserId}: {Classification} by {OverrideBy}",
            userId, classification, overrideBy);

        var result = new OverrideResult
        {
            UserId = userId,
            Success = false
        };

        try
        {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(userId))
            {
                result.Error = "User ID is required";
                return result;
            }

            if (string.IsNullOrWhiteSpace(overrideBy))
            {
                result.Error = "Override author is required";
                return result;
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                result.Error = "Override reason is required";
                return result;
            }

            // Check for existing active override
            var existingOverride = await GetOverrideAsync(userId, cancellationToken);
            if (existingOverride != null && existingOverride.IsActive)
            {
                // Expire the existing override
                await ExpireOverrideAsync(existingOverride.Id, cancellationToken);
            }

            // Create new override
            var newOverride = new ClassificationOverride
            {
                UserId = userId,
                OverrideClassification = classification,
                OverrideDate = DateTime.UtcNow,
                OverrideBy = overrideBy,
                Reason = reason,
                ExpiryDate = expiryDate,
                IsActive = true
            };

            // Save to database
            await _stateManager.SaveClassificationOverrideAsync(newOverride, cancellationToken);

            // Update cache
            lock (_cacheLock)
            {
                _overrideCache[userId] = newOverride;
            }

            // Log the override for audit
            await LogOverrideHistoryAsync(userId, existingOverride?.OverrideClassification, 
                classification, overrideBy, reason, cancellationToken);

            result.Success = true;
            result.Override = newOverride;

            _logger.LogInformation(
                "Successfully applied classification override for user {UserId}. Expires: {ExpiryDate}",
                userId, expiryDate?.ToString("yyyy-MM-dd") ?? "Never");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply classification override for user {UserId}", userId);
            result.Error = $"Failed to apply override: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Removes an active override
    /// </summary>
    public async Task<bool> RemoveOverrideAsync(
        string userId,
        string removedBy,
        string reason,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Removing classification override for user {UserId} by {RemovedBy}", userId, removedBy);

        try
        {
            var override_ = await GetOverrideAsync(userId, cancellationToken);
            if (override_ == null || !override_.IsActive)
            {
                _logger.LogWarning("No active override found for user {UserId}", userId);
                return false;
            }

            // Expire the override
            await ExpireOverrideAsync(override_.Id, cancellationToken);

            // Remove from cache
            lock (_cacheLock)
            {
                _overrideCache.Remove(userId);
            }

            // Log removal
            await LogOverrideHistoryAsync(userId, override_.OverrideClassification, 
                null, removedBy, $"Override removed: {reason}", cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove classification override for user {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Gets an active override for a user
    /// </summary>
    public async Task<ClassificationOverride?> GetOverrideAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        lock (_cacheLock)
        {
            if (_overrideCache.TryGetValue(userId, out var cachedOverride) &&
                DateTime.UtcNow - _lastCacheRefresh < _cacheExpiry)
            {
                // Check if override is still valid
                if (IsOverrideValid(cachedOverride))
                    return cachedOverride;
                
                // Remove expired override from cache
                _overrideCache.Remove(userId);
            }
        }

        // Load from database
        try
        {
            var override_ = await _stateManager.GetClassificationOverrideAsync(userId, cancellationToken);
            
            if (override_ != null && IsOverrideValid(override_))
            {
                // Update cache
                lock (_cacheLock)
                {
                    _overrideCache[userId] = override_;
                }
                return override_;
            }

            // Expire invalid override
            if (override_ != null && !IsOverrideValid(override_))
            {
                await ExpireOverrideAsync(override_.Id, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get classification override for user {UserId}", userId);
        }

        return null;
    }

    /// <summary>
    /// Gets all active overrides
    /// </summary>
    public async Task<List<ClassificationOverride>> GetAllActiveOverridesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allOverrides = await _stateManager.GetAllClassificationOverridesAsync(cancellationToken);
            var activeOverrides = new List<ClassificationOverride>();

            foreach (var override_ in allOverrides.Where(o => o.IsActive))
            {
                if (IsOverrideValid(override_))
                {
                    activeOverrides.Add(override_);
                }
                else
                {
                    // Expire invalid override
                    await ExpireOverrideAsync(override_.Id, cancellationToken);
                }
            }

            // Refresh cache
            lock (_cacheLock)
            {
                _overrideCache.Clear();
                foreach (var override_ in activeOverrides)
                {
                    _overrideCache[override_.UserId] = override_;
                }
                _lastCacheRefresh = DateTime.UtcNow;
            }

            return activeOverrides;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all active classification overrides");
            return new List<ClassificationOverride>();
        }
    }

    /// <summary>
    /// Checks if an override should be applied
    /// </summary>
    public async Task<OverrideCheckResult> CheckOverrideAsync(
        string userId,
        ProfileClassification currentClassification,
        CancellationToken cancellationToken = default)
    {
        var result = new OverrideCheckResult
        {
            UserId = userId,
            OriginalClassification = currentClassification
        };

        try
        {
            var override_ = await GetOverrideAsync(userId, cancellationToken);
            
            if (override_ != null)
            {
                result.HasOverride = true;
                result.Override = override_;
                result.EffectiveClassification = override_.OverrideClassification;
                result.OverrideReason = override_.Reason;
            }
            else
            {
                result.HasOverride = false;
                result.EffectiveClassification = currentClassification;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check override for user {UserId}", userId);
            result.Error = ex.Message;
            result.EffectiveClassification = currentClassification;
        }

        return result;
    }

    /// <summary>
    /// Gets override history for a user
    /// </summary>
    public async Task<List<ClassificationOverrideHistory>> GetOverrideHistoryAsync(
        string userId,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var history = await _stateManager.GetClassificationOverrideHistoryAsync(userId, limit, cancellationToken);
        return history.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get override history for user {UserId}", userId);
            return new List<ClassificationOverrideHistory>();
        }
    }

    /// <summary>
    /// Validates override authorization
    /// </summary>
    public async Task<bool> ValidateAuthorizationAsync(
        string overrideBy,
        ProfileClassification targetClassification,
        CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would check against AD groups, roles, etc.
        // For now, simple validation
        
        // Check if user is in authorized groups
        var authorizedGroups = new[] { "Domain Admins", "Migration Admins", "IT Support" };
        
        // TODO: Implement actual authorization check
        // This is a placeholder that always returns true
        _logger.LogDebug("Validating authorization for {User} to set classification {Classification}", 
            overrideBy, targetClassification);
        
        return await Task.FromResult(true);
    }

    /// <summary>
    /// Checks if an override is still valid
    /// </summary>
    private bool IsOverrideValid(ClassificationOverride override_)
    {
        if (!override_.IsActive)
            return false;

        if (override_.ExpiryDate.HasValue && override_.ExpiryDate.Value <= DateTime.UtcNow)
            return false;

        return true;
    }

    /// <summary>
    /// Expires an override
    /// </summary>
    private async Task ExpireOverrideAsync(int overrideId, CancellationToken cancellationToken)
    {
        try
        {
            await _stateManager.ExpireClassificationOverrideAsync(overrideId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire override {OverrideId}", overrideId);
        }
    }

    /// <summary>
    /// Logs override history
    /// </summary>
    private async Task LogOverrideHistoryAsync(
        string userId,
        ProfileClassification? oldClassification,
        ProfileClassification? newClassification,
        string changedBy,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var history = new ClassificationOverrideHistory
            {
                UserId = userId,
                OldClassification = oldClassification?.ToString(),
                NewClassification = newClassification?.ToString(),
                ChangeDate = DateTime.UtcNow,
                ChangedBy = changedBy,
                Reason = reason
            };

            await _stateManager.SaveClassificationOverrideHistoryAsync(history, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log override history for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Clears the override cache
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _overrideCache.Clear();
            _lastCacheRefresh = DateTime.MinValue;
        }
    }
}

/// <summary>
/// Represents a classification override
/// </summary>
public class ClassificationOverride
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ProfileClassification OverrideClassification { get; set; }
    public DateTime OverrideDate { get; set; }
    public string OverrideBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime? ExpiryDate { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Represents override history
/// </summary>
public class ClassificationOverrideHistory
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? OldClassification { get; set; }
    public string? NewClassification { get; set; }
    public DateTime ChangeDate { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Result of applying an override
/// </summary>
public class OverrideResult
{
    public string UserId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public ClassificationOverride? Override { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Result of checking for overrides
/// </summary>
public class OverrideCheckResult
{
    public string UserId { get; set; } = string.Empty;
    public bool HasOverride { get; set; }
    public ClassificationOverride? Override { get; set; }
    public ProfileClassification OriginalClassification { get; set; }
    public ProfileClassification EffectiveClassification { get; set; }
    public string? OverrideReason { get; set; }
    public string? Error { get; set; }
}