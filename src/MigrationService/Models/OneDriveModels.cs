using System.Text.Json.Serialization;

namespace MigrationTool.Service.Models;

/// <summary>
/// Represents the current OneDrive installation and configuration status for a user (database record)
/// </summary>
public class OneDriveStatusRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public bool IsInstalled { get; set; }
    public bool IsRunning { get; set; }
    public bool IsSignedIn { get; set; }
    public string? AccountEmail { get; set; }
    public string? PrimaryAccountId { get; set; }
    public string? SyncFolder { get; set; }
    public string SyncStatus { get; set; } = "Unknown";
    public long? AvailableSpaceMB { get; set; }
    public long? UsedSpaceMB { get; set; }
    public bool HasSyncErrors { get; set; }
    public string? ErrorDetails { get; set; }
    public DateTime LastChecked { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public long? TotalSpaceMB => AvailableSpaceMB.HasValue && UsedSpaceMB.HasValue
        ? AvailableSpaceMB.Value + UsedSpaceMB.Value
        : null;

    [JsonIgnore]
    public double? UsagePercentage => TotalSpaceMB.HasValue && TotalSpaceMB.Value > 0 && UsedSpaceMB.HasValue
        ? (double)UsedSpaceMB.Value / TotalSpaceMB.Value * 100
        : null;
}

/// <summary>
/// Represents a OneDrive account associated with a user
/// </summary>
public class OneDriveAccount
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string UserFolder { get; set; } = string.Empty;
    public bool IsPrimary { get; set; }
    public DateTime? LastSyncTime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Represents a folder synced by OneDrive (personal, business, or SharePoint)
/// </summary>
public class OneDriveSyncedFolder
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? AccountId { get; set; }
    public string LocalPath { get; set; } = string.Empty;
    public string? RemotePath { get; set; }
    public OneDriveFolderType FolderType { get; set; }
    public string? DisplayName { get; set; }
    public string? SharePointSiteUrl { get; set; }
    public string? LibraryName { get; set; }
    public long? SizeBytes { get; set; }
    public int? FileCount { get; set; }
    public bool IsSyncing { get; set; }
    public bool HasErrors { get; set; }
    public DateTime? LastSyncCheck { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public double? SizeMB => SizeBytes.HasValue ? SizeBytes.Value / 1024.0 / 1024.0 : null;
}

/// <summary>
/// Types of folders that can be synced by OneDrive
/// </summary>
public enum OneDriveFolderType
{
    Personal,
    Business,
    SharePointLibrary,
    KnownFolder
}

/// <summary>
/// Represents the Known Folder Move configuration for a user
/// </summary>
public class KnownFolderMoveStatus
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool DesktopRedirected { get; set; }
    public string? DesktopPath { get; set; }
    public bool DocumentsRedirected { get; set; }
    public string? DocumentsPath { get; set; }
    public bool PicturesRedirected { get; set; }
    public string? PicturesPath { get; set; }
    public string? ConfigurationSource { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public int RedirectedFolderCount =>
        (DesktopRedirected ? 1 : 0) +
        (DocumentsRedirected ? 1 : 0) +
        (PicturesRedirected ? 1 : 0);
}

/// <summary>
/// Represents a sync error encountered by OneDrive
/// </summary>
public class OneDriveSyncError
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? FilePath { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public bool IsRecoverable { get; set; } = true;
    public bool AttemptedRecovery { get; set; }
    public string? RecoveryResult { get; set; }
    public DateTime ErrorTime { get; set; }
    public DateTime? ResolvedTime { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public bool IsResolved => ResolvedTime.HasValue;

    [JsonIgnore]
    public TimeSpan? ResolutionDuration => ResolvedTime.HasValue
        ? ResolvedTime.Value - ErrorTime
        : null;
}

/// <summary>
/// Aggregated OneDrive status information for a user
/// </summary>
public class OneDriveUserSummary
{
    public string UserId { get; set; } = string.Empty;
    public OneDriveStatusRecord? Status { get; set; }
    public List<OneDriveAccount> Accounts { get; set; } = new();
    public List<OneDriveSyncedFolder> SyncedFolders { get; set; } = new();
    public KnownFolderMoveStatus? KnownFolderStatus { get; set; }
    public List<OneDriveSyncError> RecentErrors { get; set; } = new();

    [JsonIgnore]
    public long TotalSyncedSizeBytes => SyncedFolders.Sum(f => f.SizeBytes ?? 0);

    [JsonIgnore]
    public double TotalSyncedSizeMB => TotalSyncedSizeBytes / 1024.0 / 1024.0;

    [JsonIgnore]
    public int TotalFileCount => SyncedFolders.Sum(f => f.FileCount ?? 0);

    [JsonIgnore]
    public bool HasAnyErrors => (Status?.HasSyncErrors ?? false) ||
                                SyncedFolders.Any(f => f.HasErrors) ||
                                RecentErrors.Any(e => !e.IsResolved);
}
