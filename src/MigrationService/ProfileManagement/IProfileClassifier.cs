using System.Runtime.Versioning;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.ProfileManagement;

/// <summary>
/// Interface for profile classification functionality
/// </summary>
[SupportedOSPlatform("windows")]
public interface IProfileClassifier
{
    /// <summary>
    /// Classifies a user profile based on its metrics and characteristics
    /// </summary>
    /// <param name="profile">The user profile to classify</param>
    /// <param name="metrics">The profile metrics</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Classification result</returns>
    Task<ProfileClassificationResult> ClassifyProfileAsync(
        UserProfile profile, 
        ProfileMetrics metrics,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a manual classification override
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="classification">Target classification</param>
    /// <param name="overrideBy">Who is applying the override</param>
    /// <param name="reason">Reason for override</param>
    /// <param name="expiryDate">Optional expiry date for override</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Override result</returns>
    Task<OverrideResult> ApplyManualOverrideAsync(
        string userId,
        ProfileClassification classification,
        string overrideBy,
        string reason,
        DateTime? expiryDate = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets classification history for a user
    /// </summary>
    /// <param name="userId">User identifier</param>
    /// <param name="limit">Maximum number of entries to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Classification history entries</returns>
    Task<IEnumerable<ClassificationHistoryEntry>> GetClassificationHistoryAsync(
        string userId,
        int? limit = null,
        CancellationToken cancellationToken = default);
}