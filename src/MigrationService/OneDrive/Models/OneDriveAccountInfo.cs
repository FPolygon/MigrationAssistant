namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Detailed information about a OneDrive for Business account
/// </summary>
public class OneDriveAccountInfo
{
    /// <summary>
    /// Unique identifier for the account (e.g., "Business1")
    /// </summary>
    public string AccountId { get; set; } = string.Empty;

    /// <summary>
    /// Email address associated with the account
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the account owner
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Primary OneDrive folder path
    /// </summary>
    public string UserFolder { get; set; } = string.Empty;

    /// <summary>
    /// All folders synced by this account (including SharePoint libraries)
    /// </summary>
    public List<OneDriveSyncFolder> SyncedFolders { get; set; } = new();

    /// <summary>
    /// Known Folder Move configuration status
    /// </summary>
    public KnownFolderMoveStatus? KfmStatus { get; set; }

    /// <summary>
    /// Whether this is the primary business account
    /// </summary>
    public bool IsPrimary { get; set; }

    /// <summary>
    /// Last time files were synchronized
    /// </summary>
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// Total space used in bytes
    /// </summary>
    public long? UsedSpaceBytes { get; set; }

    /// <summary>
    /// Total available space in bytes
    /// </summary>
    public long? TotalSpaceBytes { get; set; }

    /// <summary>
    /// Available space in megabytes
    /// </summary>
    public long AvailableSpaceMB => TotalSpaceBytes.HasValue && UsedSpaceBytes.HasValue
        ? (TotalSpaceBytes.Value - UsedSpaceBytes.Value) / (1024 * 1024)
        : -1;

    /// <summary>
    /// Whether the account has any sync errors
    /// </summary>
    public bool HasSyncErrors { get; set; }

    /// <summary>
    /// Sync error details if any
    /// </summary>
    public string? SyncErrorDetails { get; set; }
}
