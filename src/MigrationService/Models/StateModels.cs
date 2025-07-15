namespace MigrationTool.Service.Models;

// Enums
public enum MigrationStateType
{
    NotStarted,
    Initializing,
    WaitingForUser,
    BackupInProgress,
    BackupCompleted,
    SyncInProgress,
    ReadyForReset,
    Cancelled,
    Failed,
    Escalated
}

public enum BackupStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    PartialSuccess
}

public enum EscalationTriggerType
{
    QuotaExceeded,
    SyncError,
    LargePST,
    Timeout,
    UserRequest,
    BackupFailure,
    MultipleFailures,
    MaxDelaysExceeded
}

public enum ProfileType
{
    Local,
    Domain,
    AzureAD,
    Hybrid
}

// Enhanced model classes
public class UserProfile
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? DomainName { get; set; }
    public string ProfilePath { get; set; } = string.Empty;
    public ProfileType ProfileType { get; set; } = ProfileType.Local;
    public DateTime LastLoginTime { get; set; }
    public bool IsActive { get; set; }
    public long ProfileSizeBytes { get; set; }
    public bool RequiresBackup { get; set; } = true;
    public int BackupPriority { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MigrationState
{
    public string UserId { get; set; } = string.Empty;
    public MigrationStateType State { get; set; } = MigrationStateType.NotStarted;
    public string Status { get; set; } = "Active";
    public int Progress { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public string? AttentionReason { get; set; }
    public int DelayCount { get; set; }
    public bool IsBlocking { get; set; } = true;

    public bool RequiresAttention() => !string.IsNullOrEmpty(AttentionReason);
    public bool NeedsProgressUpdate() => State == MigrationStateType.BackupInProgress;
}

public class BackupOperation
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string OperationId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public BackupStatus Status { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastUpdated { get; set; }
    public int Progress { get; set; }
    public long BytesTotal { get; set; }
    public long BytesTransferred { get; set; }
    public int ItemsTotal { get; set; }
    public int ItemsCompleted { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public string? ManifestPath { get; set; }
}

public class OneDriveSyncStatusRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public bool IsSignedIn { get; set; }
    public string? AccountEmail { get; set; }
    public string? SyncFolderPath { get; set; }
    public string SyncStatus { get; set; } = "Unknown";
    public long? QuotaTotalMB { get; set; }
    public long? QuotaUsedMB { get; set; }
    public long? QuotaAvailableMB { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public string? LastSyncError { get; set; }
    public int ErrorCount { get; set; }
    public DateTime CheckedAt { get; set; }
}

public class ITEscalation
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public EscalationTriggerType TriggerType { get; set; }
    public string TriggerReason { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? TicketNumber { get; set; }
    public string Status { get; set; } = "Open";
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
    public bool AutoTriggered { get; set; } = true;
    
    /// <summary>
    /// Alias for TriggerReason to maintain compatibility
    /// </summary>
    public string Reason
    {
        get => TriggerReason;
        set => TriggerReason = value;
    }
}

public class StateHistoryEntry
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? OldState { get; set; }
    public string NewState { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public string ChangedBy { get; set; } = "SYSTEM";
    public DateTime ChangedAt { get; set; }
    public string? Details { get; set; }
}

public class DelayRequest
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public int RequestedDelayHours { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTime? ApprovedAt { get; set; }
    public DateTime? NewDeadline { get; set; }
}

public class ProviderResult
{
    public int Id { get; set; }
    public int BackupOperationId { get; set; }
    public string Category { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int ItemCount { get; set; }
    public long SizeMB { get; set; }
    public int Duration { get; set; }
    public string? Details { get; set; }
    public string? Errors { get; set; }
}

// Aggregated models for queries
public class UserMigrationSummary
{
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public MigrationStateType State { get; set; }
    public int OverallProgress { get; set; }
    public bool IsBlocking { get; set; }
    public Dictionary<string, BackupStatus> CategoryStatuses { get; set; } = new();
    public long TotalBackupSizeMB { get; set; }
    public DateTime? EstimatedCompletion { get; set; }
}

public class MigrationReadinessStatus
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int CompletedUsers { get; set; }
    public int BlockingUsers { get; set; }
    public List<string> BlockingUserNames { get; set; } = new();
    public List<string> CompletedUserNames { get; set; } = new();
    public bool CanReset { get; set; }
    public DateTime? EstimatedReadyTime { get; set; }
}
