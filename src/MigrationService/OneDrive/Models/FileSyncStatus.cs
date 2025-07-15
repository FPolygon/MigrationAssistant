namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Represents the sync status of a file in OneDrive
/// </summary>
public class FileSyncStatus
{
    /// <summary>
    /// The file path
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// The sync state of the file
    /// </summary>
    public FileSyncState State { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// Whether the file is pinned (always available offline)
    /// </summary>
    public bool IsPinned { get; set; }

    /// <summary>
    /// Error message if sync failed
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Represents the sync state of a file
/// </summary>
public enum FileSyncState
{
    /// <summary>
    /// Unknown state
    /// </summary>
    Unknown,

    /// <summary>
    /// File exists only locally, not yet uploaded to OneDrive
    /// </summary>
    LocalOnly,

    /// <summary>
    /// File is currently being uploaded to OneDrive
    /// </summary>
    Uploading,

    /// <summary>
    /// File is fully synced to OneDrive (may be placeholder locally)
    /// </summary>
    InSync,

    /// <summary>
    /// File is in OneDrive but marked as online-only (placeholder)
    /// </summary>
    CloudOnly,

    /// <summary>
    /// File is in OneDrive and also stored locally
    /// </summary>
    LocallyAvailable,

    /// <summary>
    /// File sync has errors
    /// </summary>
    Error
}