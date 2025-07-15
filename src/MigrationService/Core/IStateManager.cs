using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;

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

    // OneDrive sync tracking (legacy)
    Task UpdateOneDriveSyncStatusAsync(OneDriveSyncStatus status, CancellationToken cancellationToken);
    Task<OneDriveSyncStatus?> GetOneDriveSyncStatusAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncStatus>> GetUsersWithSyncErrorsAsync(CancellationToken cancellationToken);

    // OneDrive status management (new detailed tracking)
    Task SaveOneDriveStatusAsync(OneDriveStatusRecord status, CancellationToken cancellationToken);
    Task<OneDriveStatusRecord?> GetOneDriveStatusAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveStatusRecord>> GetAllOneDriveStatusesAsync(CancellationToken cancellationToken);

    // OneDrive account management
    Task SaveOneDriveAccountAsync(OneDriveAccount account, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveAccount>> GetOneDriveAccountsAsync(string userId, CancellationToken cancellationToken);
    Task<OneDriveAccount?> GetPrimaryOneDriveAccountAsync(string userId, CancellationToken cancellationToken);
    Task DeleteOneDriveAccountAsync(string userId, string accountId, CancellationToken cancellationToken);

    // OneDrive synced folder management
    Task SaveOneDriveSyncedFolderAsync(OneDriveSyncedFolder folder, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncedFolder>> GetOneDriveSyncedFoldersAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncedFolder>> GetOneDriveSyncedFoldersForAccountAsync(string userId, string accountId, CancellationToken cancellationToken);
    Task DeleteOneDriveSyncedFolderAsync(int folderId, CancellationToken cancellationToken);
    Task UpdateSyncedFolderStatusAsync(int folderId, bool isSyncing, bool hasErrors, CancellationToken cancellationToken);

    // Known Folder Move management
    Task SaveKnownFolderMoveStatusAsync(KnownFolderMoveStatus status, CancellationToken cancellationToken);
    Task<KnownFolderMoveStatus?> GetKnownFolderMoveStatusAsync(string userId, string accountId, CancellationToken cancellationToken);
    Task<IEnumerable<KnownFolderMoveStatus>> GetAllKnownFolderMoveStatusesAsync(string userId, CancellationToken cancellationToken);

    // OneDrive sync error management
    Task SaveOneDriveSyncErrorAsync(OneDriveSyncError error, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncError>> GetOneDriveSyncErrorsAsync(string userId, int? limit = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<OneDriveSyncError>> GetUnresolvedSyncErrorsAsync(string userId, CancellationToken cancellationToken);
    Task MarkSyncErrorResolvedAsync(int errorId, string? recoveryResult = null, CancellationToken cancellationToken = default);
    Task<int> GetActiveSyncErrorCountAsync(string userId, CancellationToken cancellationToken);

    // OneDrive aggregated queries
    Task<OneDriveUserSummary> GetOneDriveUserSummaryAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<string>> GetUsersWithOneDriveErrorsAsync(CancellationToken cancellationToken);
    Task<Dictionary<string, long>> GetOneDriveStorageUsageAsync(CancellationToken cancellationToken);
    Task<bool> IsUserReadyForOneDriveBackupAsync(string userId, CancellationToken cancellationToken);

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

    // Classification management
    Task SaveUserClassificationAsync(string userId, ProfileClassification classification, double confidence, string reason, string? ruleSetName = null, CancellationToken cancellationToken = default);
    Task<UserClassificationRecord?> GetUserClassificationAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<UserClassificationRecord>> GetAllClassificationsAsync(CancellationToken cancellationToken = default);
    Task SaveClassificationHistoryAsync(string userId, ProfileClassification? oldClassification, ProfileClassification newClassification, string reason, Dictionary<string, object>? activitySnapshot = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ClassificationHistoryEntry>> GetClassificationHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default);

    // Classification override management
    Task SaveClassificationOverrideAsync(ClassificationOverride override_, CancellationToken cancellationToken = default);
    Task<ClassificationOverride?> GetClassificationOverrideAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ClassificationOverride>> GetAllClassificationOverridesAsync(CancellationToken cancellationToken = default);
    Task ExpireClassificationOverrideAsync(int overrideId, CancellationToken cancellationToken = default);
    Task SaveClassificationOverrideHistoryAsync(ClassificationOverrideHistory history, CancellationToken cancellationToken = default);
    Task<IEnumerable<ClassificationOverrideHistory>> GetClassificationOverrideHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default);

    // Maintenance operations
    Task CleanupStaleOperationsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken);
    Task ArchiveCompletedMigrationsAsync(int daysToKeep, CancellationToken cancellationToken);
    Task<Dictionary<string, object>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken);
}
