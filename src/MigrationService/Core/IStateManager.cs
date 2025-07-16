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
    Task UpdateOneDriveSyncStatusAsync(OneDriveSyncStatusRecord status, CancellationToken cancellationToken);
    Task<OneDriveSyncStatusRecord?> GetOneDriveSyncStatusAsync(string userId, CancellationToken cancellationToken);
    Task<IEnumerable<OneDriveSyncStatusRecord>> GetUsersWithSyncErrorsAsync(CancellationToken cancellationToken);

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

    // Sync operation management
    Task<int> CreateSyncOperationAsync(SyncOperation operation, CancellationToken cancellationToken);
    Task UpdateSyncOperationAsync(SyncOperation operation, CancellationToken cancellationToken);
    Task<SyncOperation?> GetSyncOperationAsync(int operationId, CancellationToken cancellationToken);
    Task<SyncOperation?> GetActiveSyncOperationAsync(string userSid, string folderPath, CancellationToken cancellationToken);
    Task<IEnumerable<SyncOperation>> GetSyncOperationsAsync(string userSid, CancellationToken cancellationToken);
    Task<IEnumerable<SyncOperation>> GetPendingSyncOperationsAsync(CancellationToken cancellationToken);
    Task IncrementSyncRetryCountAsync(int syncOperationId, CancellationToken cancellationToken);

    // Sync error management
    Task<int> RecordSyncErrorAsync(SyncError error, CancellationToken cancellationToken);
    Task<IEnumerable<SyncError>> GetSyncErrorsAsync(int syncOperationId, CancellationToken cancellationToken);
    Task<IEnumerable<SyncError>> GetUnresolvedSyncOperationErrorsAsync(string userSid, CancellationToken cancellationToken);
    Task MarkSyncOperationErrorResolvedAsync(int errorId, CancellationToken cancellationToken);
    Task EscalateSyncErrorsAsync(int syncOperationId, CancellationToken cancellationToken);
    Task<int> GetSyncErrorCountAsync(string userSid, bool unresolvedOnly = true, CancellationToken cancellationToken = default);

    // Maintenance operations
    Task CleanupStaleOperationsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken);
    Task ArchiveCompletedMigrationsAsync(int daysToKeep, CancellationToken cancellationToken);
    Task<Dictionary<string, object>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken);

    #region Quota Management (Phase 3.3)

    // Quota status management
    Task SaveQuotaStatusAsync(QuotaStatusRecord status, CancellationToken cancellationToken = default);
    Task<QuotaStatusRecord?> GetQuotaStatusAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<QuotaStatusRecord>> GetAllQuotaStatusesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<QuotaStatusRecord>> GetQuotaStatusByHealthLevelAsync(QuotaHealthLevel healthLevel, CancellationToken cancellationToken = default);

    // Backup requirements management
    Task SaveBackupRequirementsAsync(BackupRequirementsRecord requirements, CancellationToken cancellationToken = default);
    Task<BackupRequirementsRecord?> GetBackupRequirementsAsync(string userId, CancellationToken cancellationToken = default);
    Task<BackupRequirementsRecord?> GetLatestBackupRequirementsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BackupRequirementsRecord>> GetAllBackupRequirementsAsync(CancellationToken cancellationToken = default);

    // Quota warning management
    Task<int> CreateQuotaWarningAsync(QuotaWarning warning, CancellationToken cancellationToken = default);
    Task UpdateQuotaWarningAsync(QuotaWarning warning, CancellationToken cancellationToken = default);
    Task<QuotaWarning?> GetQuotaWarningAsync(int warningId, CancellationToken cancellationToken = default);
    Task<List<QuotaWarning>> GetQuotaWarningsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<QuotaWarning>> GetUnresolvedQuotaWarningsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<QuotaWarning>> GetQuotaWarningsByLevelAsync(QuotaWarningLevel level, CancellationToken cancellationToken = default);
    Task<List<QuotaWarning>> GetQuotaWarningsByTypeAsync(QuotaWarningType type, CancellationToken cancellationToken = default);
    Task ResolveQuotaWarningAsync(int warningId, string resolutionNotes, CancellationToken cancellationToken = default);

    // Quota escalation management
    Task<int> CreateQuotaEscalationAsync(QuotaEscalation escalation, CancellationToken cancellationToken = default);
    Task UpdateQuotaEscalationAsync(QuotaEscalation escalation, CancellationToken cancellationToken = default);
    Task<QuotaEscalation?> GetQuotaEscalationAsync(int escalationId, CancellationToken cancellationToken = default);
    Task<List<QuotaEscalation>> GetQuotaEscalationsAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<QuotaEscalation>> GetUnresolvedQuotaEscalationsAsync(CancellationToken cancellationToken = default);
    Task<List<QuotaEscalation>> GetQuotaEscalationsBySeverityAsync(QuotaWarningLevel severity, CancellationToken cancellationToken = default);
    Task ResolveQuotaEscalationAsync(int escalationId, string ticketNumber, string resolutionNotes, CancellationToken cancellationToken = default);

    // Quota analytics and reporting
    Task<Dictionary<QuotaHealthLevel, int>> GetQuotaHealthDistributionAsync(CancellationToken cancellationToken = default);
    Task<List<string>> GetUsersRequiringQuotaAttentionAsync(CancellationToken cancellationToken = default);
    Task<long> GetTotalRequiredBackupSpaceAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, long>> GetBackupSpaceByUserAsync(CancellationToken cancellationToken = default);
    Task<int> GetActiveQuotaWarningCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetUnresolvedQuotaEscalationCountAsync(CancellationToken cancellationToken = default);

    #endregion
}
