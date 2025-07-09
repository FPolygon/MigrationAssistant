using System.Runtime.Versioning;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Interface for managing manual classification overrides for user profiles
/// </summary>
[SupportedOSPlatform("windows")]
public interface IClassificationOverrideManager
{
    /// <summary>
    /// Applies a manual classification override
    /// </summary>
    Task<OverrideResult> ApplyOverrideAsync(
        string userId,
        ProfileClassification classification,
        string overrideBy,
        string reason,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an active override
    /// </summary>
    Task<bool> RemoveOverrideAsync(
        string userId,
        string removedBy,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an active override for a user
    /// </summary>
    Task<ClassificationOverride?> GetOverrideAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active overrides
    /// </summary>
    Task<List<ClassificationOverride>> GetAllActiveOverridesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an override should be applied
    /// </summary>
    Task<OverrideCheckResult> CheckOverrideAsync(
        string userId,
        ProfileClassification currentClassification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets override history for a user
    /// </summary>
    Task<List<ClassificationOverrideHistory>> GetOverrideHistoryAsync(
        string userId,
        int? limit = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates override authorization
    /// </summary>
    Task<bool> ValidateAuthorizationAsync(
        string overrideBy,
        ProfileClassification targetClassification,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the override cache
    /// </summary>
    void ClearCache();
}
