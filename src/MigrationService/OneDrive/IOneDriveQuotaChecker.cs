using MigrationTool.Service.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Interface for checking OneDrive quota status and feasibility
/// </summary>
public interface IOneDriveQuotaChecker
{
    /// <summary>
    /// Performs a comprehensive quota status check for a user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed quota status including health assessment</returns>
    Task<QuotaStatus> CheckQuotaStatusAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if a backup operation is feasible given current quota
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="requiredMB">Required space in megabytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if backup is feasible, false otherwise</returns>
    Task<bool> ValidateBackupFeasibilityAsync(string userSid, long requiredMB, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the quota health assessment for a user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Quota health level and details</returns>
    Task<QuotaHealthLevel> GetQuotaHealthAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates how much additional space is needed for a backup
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="requiredMB">Required space in megabytes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Additional space needed in MB, or 0 if sufficient space available</returns>
    Task<long> CalculateSpaceShortfallAsync(string userSid, long requiredMB, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates recommendations for resolving quota issues
    /// </summary>
    /// <param name="quotaStatus">Current quota status</param>
    /// <param name="requiredMB">Required space for backup</param>
    /// <returns>List of recommended actions</returns>
    List<string> GenerateRecommendations(QuotaStatus quotaStatus, long requiredMB);

    /// <summary>
    /// Checks if a user is approaching quota limits
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if user is approaching quota limits</returns>
    Task<bool> IsApproachingQuotaLimitAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates quota configuration and OneDrive setup
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of configuration issues found</returns>
    Task<List<string>> ValidateQuotaConfigurationAsync(string userSid, CancellationToken cancellationToken = default);
}
