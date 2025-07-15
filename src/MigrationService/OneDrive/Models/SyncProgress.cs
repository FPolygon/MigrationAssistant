namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Represents the synchronization progress for a folder
/// </summary>
public class SyncProgress
{
    /// <summary>
    /// The folder path being synchronized
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// Current sync status
    /// </summary>
    public OneDriveSyncStatus Status { get; set; }

    /// <summary>
    /// Percentage of sync completion (0-100)
    /// </summary>
    public double PercentComplete { get; set; }

    /// <summary>
    /// Number of files synchronized
    /// </summary>
    public int FilesSynced { get; set; }

    /// <summary>
    /// Total number of files to sync
    /// </summary>
    public int TotalFiles { get; set; }

    /// <summary>
    /// Bytes synchronized
    /// </summary>
    public long BytesSynced { get; set; }

    /// <summary>
    /// Total bytes to sync
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Files currently being processed
    /// </summary>
    public List<string> ActiveFiles { get; set; } = new();

    /// <summary>
    /// Files that failed to sync
    /// </summary>
    public List<OneDriveSyncError> Errors { get; set; } = new();

    /// <summary>
    /// Estimated time remaining
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// When the sync started
    /// </summary>
    public DateTime? SyncStartTime { get; set; }

    /// <summary>
    /// Whether the sync is complete
    /// </summary>
    public bool IsComplete => Status == OneDriveSyncStatus.UpToDate ||
                             (TotalFiles > 0 && FilesSynced >= TotalFiles);
}

/// <summary>
/// Represents a synchronization error
/// </summary>
public class OneDriveSyncError
{
    /// <summary>
    /// File path that failed to sync
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// Error message
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Error code if available
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// When the error occurred
    /// </summary>
    public DateTime ErrorTime { get; set; }

    /// <summary>
    /// Whether this error is recoverable
    /// </summary>
    public bool IsRecoverable { get; set; }
}
