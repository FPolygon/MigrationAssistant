namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Represents the current status of OneDrive for a user
/// </summary>
public class OneDriveStatus
{
    /// <summary>
    /// Whether OneDrive is installed on the system
    /// </summary>
    public bool IsInstalled { get; set; }

    /// <summary>
    /// Whether the OneDrive process is currently running
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Whether the user is signed into OneDrive
    /// </summary>
    public bool IsSignedIn { get; set; }

    /// <summary>
    /// The email address of the signed-in account
    /// </summary>
    public string? AccountEmail { get; set; }

    /// <summary>
    /// The primary OneDrive sync folder path
    /// </summary>
    public string? SyncFolder { get; set; }

    /// <summary>
    /// Current sync status
    /// </summary>
    public OneDriveSyncStatus SyncStatus { get; set; }

    /// <summary>
    /// Detailed account information including all sync folders
    /// </summary>
    public OneDriveAccountInfo? AccountInfo { get; set; }

    /// <summary>
    /// Any error details if status checks failed
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// When this status was last checked
    /// </summary>
    public DateTime LastChecked { get; set; }
}

/// <summary>
/// OneDrive synchronization status
/// </summary>
public enum OneDriveSyncStatus
{
    /// <summary>
    /// Status cannot be determined
    /// </summary>
    Unknown,

    /// <summary>
    /// All files are synchronized
    /// </summary>
    UpToDate,

    /// <summary>
    /// Currently synchronizing files
    /// </summary>
    Syncing,

    /// <summary>
    /// Synchronization is paused
    /// </summary>
    Paused,

    /// <summary>
    /// Synchronization error occurred
    /// </summary>
    Error,

    /// <summary>
    /// Not signed in to OneDrive
    /// </summary>
    NotSignedIn,

    /// <summary>
    /// Authentication token expired
    /// </summary>
    AuthenticationRequired
}
