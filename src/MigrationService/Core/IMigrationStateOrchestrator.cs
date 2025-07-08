using MigrationTool.Service.Models;

namespace MigrationTool.Service.Core;

/// <summary>
/// Interface for orchestrating migration state transitions
/// </summary>
public interface IMigrationStateOrchestrator
{
    /// <summary>
    /// Process automatic state transitions for all active migrations
    /// </summary>
    Task ProcessAutomaticTransitionsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Process state transition for a specific user
    /// </summary>
    Task ProcessUserTransitionAsync(MigrationState migration, CancellationToken cancellationToken);

    /// <summary>
    /// Handle backup completion notification
    /// </summary>
    Task HandleBackupCompletedAsync(string userId, string operationId, bool success, CancellationToken cancellationToken);

    /// <summary>
    /// Get migration statistics
    /// </summary>
    Task<MigrationStatistics> GetStatisticsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Refresh user profiles and initialize migration states for new users
    /// </summary>
    Task RefreshUserProfilesAsync(CancellationToken cancellationToken);
}