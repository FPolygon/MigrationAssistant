using MigrationTool.Service.Models;

namespace MigrationTool.Service.Core;

public interface IStateManager : IDisposable
{
    // Initialization and health
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<bool> CheckHealthAsync(CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);

    // User profile management
    Task<IEnumerable<UserProfile>> GetUserProfilesAsync(CancellationToken cancellationToken);
    Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken);
    Task UpdateUserProfileAsync(UserProfile profile, CancellationToken cancellationToken);
    Task<IEnumerable<UserProfile>> GetActiveUserProfilesAsync(CancellationToken cancellationToken);

    // Migration state management
    Task<IEnumerable<MigrationState>> GetActiveMigrationsAsync(CancellationToken cancellationToken);
    Task<MigrationState?> GetMigrationStateAsync(string userId, CancellationToken cancellationToken);
    Task UpdateMigrationStateAsync(MigrationState state, CancellationToken cancellationToken);
    Task<bool> TransitionStateAsync(string userId, MigrationStateType newState, string reason, CancellationToken cancellationToken);

    // Backup operation tracking
    Task<string> CreateBackupOperationAsync(BackupOperation operation, CancellationToken cancellationToken);
    Task UpdateBackupOperationAsync(BackupOperation operation, CancellationToken cancellationToken);
    Task<BackupOperation?> GetBackupOperationAsync(string operationId, CancellationToken cancellationToken);
    Task<IEnumerable<BackupOperation>> GetUserBackupOperationsAsync(string userId, CancellationToken cancellationToken);
    Task RecordProviderResultAsync(ProviderResult result, CancellationToken cancellationToken);

    // OneDrive sync tracking
    Task UpdateOneDriveSyncStatusAsync(OneDriveSyncStatus status, CancellationToken cancellationToken);
    Task<OneDriveSyncStatus?> GetOneDriveSyncStatusAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncStatus>> GetUsersWithSyncErrorsAsync(CancellationToken cancellationToken);

    // IT escalation management
    Task<int> CreateEscalationAsync(ITEscalation escalation, CancellationToken cancellationToken);
    Task UpdateEscalationAsync(ITEscalation escalation, CancellationToken cancellationToken);
    Task<IEnumerable<ITEscalation>> GetOpenEscalationsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<ITEscalation>> GetUserEscalationsAsync(string userId, CancellationToken cancellationToken);

    // Delay request management
    Task<int> CreateDelayRequestAsync(DelayRequest request, CancellationToken cancellationToken);
    Task ApproveDelayRequestAsync(int requestId, DateTime newDeadline, CancellationToken cancellationToken);
    Task<IEnumerable<DelayRequest>> GetPendingDelayRequestsAsync(CancellationToken cancellationToken);
    Task<int> GetUserDelayCountAsync(string userId, CancellationToken cancellationToken);

    // State history and audit
    Task<IEnumerable<StateHistoryEntry>> GetStateHistoryAsync(string userId, CancellationToken cancellationToken);
    Task RecordSystemEventAsync(string eventType, string severity, string message, string? details = null, string? userId = null, CancellationToken cancellationToken = default);

    // Aggregated queries
    Task<bool> AreAllUsersReadyForResetAsync(CancellationToken cancellationToken);
    Task<MigrationReadinessStatus> GetMigrationReadinessAsync(CancellationToken cancellationToken);
    Task<IEnumerable<UserMigrationSummary>> GetMigrationSummariesAsync(CancellationToken cancellationToken);
    Task<IEnumerable<string>> GetUsersRequiringAttentionAsync(CancellationToken cancellationToken);

    // Maintenance operations
    Task CleanupStaleOperationsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken);
    Task ArchiveCompletedMigrationsAsync(int daysToKeep, CancellationToken cancellationToken);
    Task<Dictionary<string, object>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken);
}