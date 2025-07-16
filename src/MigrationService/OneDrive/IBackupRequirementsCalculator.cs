using MigrationTool.Service.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Interface for calculating backup space requirements
/// </summary>
public interface IBackupRequirementsCalculator
{
    /// <summary>
    /// Calculates the total space required for backing up a user's data
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed backup requirements including space breakdown</returns>
    Task<BackupRequirements> CalculateRequiredSpaceMBAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates backup space requirements for a specific user profile
    /// </summary>
    /// <param name="profile">The user profile to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed backup requirements</returns>
    Task<BackupRequirements> EstimateBackupSizeAsync(UserProfile profile, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculates compression factor for a specific folder path
    /// </summary>
    /// <param name="folderPath">The folder path to analyze</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated compression factor (0.0 to 1.0)</returns>
    Task<double> GetCompressionFactorAsync(string folderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the detailed size breakdown for a user's profile
    /// </summary>
    /// <param name="profilePath">The user's profile path</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed size breakdown by category</returns>
    Task<BackupSizeBreakdown> GetSizeBreakdownAsync(string profilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates if the calculated requirements are reasonable
    /// </summary>
    /// <param name="requirements">The backup requirements to validate</param>
    /// <returns>True if requirements are within reasonable limits</returns>
    bool ValidateRequirements(BackupRequirements requirements);
}
