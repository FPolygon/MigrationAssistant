namespace MigrationTool.Service.OneDrive.Models;

/// <summary>
/// Represents a folder synchronized by OneDrive
/// </summary>
public class OneDriveSyncFolder
{
    /// <summary>
    /// Local path of the synchronized folder
    /// </summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>
    /// Remote URL or path in OneDrive/SharePoint
    /// </summary>
    public string? RemotePath { get; set; }

    /// <summary>
    /// Type of sync folder
    /// </summary>
    public SyncFolderType FolderType { get; set; }

    /// <summary>
    /// Display name of the folder (e.g., "Documents - Contoso")
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// SharePoint site URL if this is a SharePoint library
    /// </summary>
    public string? SharePointSiteUrl { get; set; }

    /// <summary>
    /// Library name if this is a SharePoint document library
    /// </summary>
    public string? LibraryName { get; set; }

    /// <summary>
    /// Type of SharePoint library
    /// </summary>
    public string? LibraryType { get; set; }

    /// <summary>
    /// Owner name of the SharePoint library
    /// </summary>
    public string? OwnerName { get; set; }

    /// <summary>
    /// Whether this folder is currently syncing
    /// </summary>
    public bool IsSyncing { get; set; }

    /// <summary>
    /// Whether this folder has sync errors
    /// </summary>
    public bool HasErrors { get; set; }

    /// <summary>
    /// Size of the folder in bytes
    /// </summary>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// Number of files in the folder
    /// </summary>
    public int? FileCount { get; set; }
}

/// <summary>
/// Type of synchronized folder
/// </summary>
public enum SyncFolderType
{
    /// <summary>
    /// Personal OneDrive folder
    /// </summary>
    Personal,

    /// <summary>
    /// OneDrive for Business folder
    /// </summary>
    Business,

    /// <summary>
    /// SharePoint document library
    /// </summary>
    SharePointLibrary,

    /// <summary>
    /// SharePoint site
    /// </summary>
    SharePointSite,

    /// <summary>
    /// Known folder (Desktop, Documents, Pictures)
    /// </summary>
    KnownFolder,

    /// <summary>
    /// Other type of sync folder
    /// </summary>
    Other
}
