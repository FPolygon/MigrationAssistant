namespace MigrationTool.Service.Models;

/// <summary>
/// Represents a sync operation for tracking upload progress
/// </summary>
public class SyncOperation
{
    public int Id { get; set; }
    public string UserSid { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public SyncOperationStatus Status { get; set; }
    public int? FilesTotal { get; set; }
    public int? FilesUploaded { get; set; }
    public long? BytesTotal { get; set; }
    public long? BytesUploaded { get; set; }
    public int? LocalOnlyFiles { get; set; }
    public int ErrorCount { get; set; }
    public int RetryCount { get; set; }
    public DateTime? LastRetryTime { get; set; }
    public int? EstimatedTimeRemaining { get; set; } // seconds
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Related sync errors
    /// </summary>
    public List<SyncError> Errors { get; set; } = new();

    /// <summary>
    /// Calculates the progress percentage
    /// </summary>
    public double ProgressPercentage => FilesTotal > 0 
        ? (double)(FilesUploaded ?? 0) / FilesTotal.Value * 100 
        : 0;

    /// <summary>
    /// Determines if the sync operation is complete
    /// </summary>
    public bool IsComplete => Status == SyncOperationStatus.Completed || 
                             Status == SyncOperationStatus.Failed;

    /// <summary>
    /// Gets the duration of the sync operation
    /// </summary>
    public TimeSpan? Duration => EndTime.HasValue 
        ? EndTime.Value - StartTime 
        : (IsComplete ? null : DateTime.UtcNow - StartTime);
}

/// <summary>
/// Status of a sync operation
/// </summary>
public enum SyncOperationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    Cancelled,
    TimedOut,
    RequiresIntervention
}

/// <summary>
/// Represents a sync error for a specific file
/// </summary>
public class SyncError
{
    public int Id { get; set; }
    public int SyncOperationId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime ErrorTime { get; set; }
    public int RetryAttempts { get; set; }
    public bool IsResolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public bool EscalatedToIT { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Determines if this error should be escalated based on retry attempts
    /// </summary>
    public bool ShouldEscalate => RetryAttempts >= 3 && !IsResolved && !EscalatedToIT;
}