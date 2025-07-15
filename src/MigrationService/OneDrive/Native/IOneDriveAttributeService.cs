using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Service for determining OneDrive file sync states from Windows file attributes
/// </summary>
[SupportedOSPlatform("windows")]
public interface IOneDriveAttributeService
{
    /// <summary>
    /// Determines the OneDrive sync state of a file based on its attributes
    /// </summary>
    /// <param name="fileAttributes">The file attributes from Windows</param>
    /// <returns>The determined sync state</returns>
    FileSyncState GetFileSyncState(FileAttributes fileAttributes);

    /// <summary>
    /// Determines if a file is pinned (always available offline)
    /// </summary>
    /// <param name="fileAttributes">The file attributes from Windows</param>
    /// <returns>True if the file is pinned, false otherwise</returns>
    bool IsFilePinned(FileAttributes fileAttributes);

    /// <summary>
    /// Determines if a file is a cloud-only placeholder
    /// </summary>
    /// <param name="fileAttributes">The file attributes from Windows</param>
    /// <returns>True if the file is cloud-only, false otherwise</returns>
    bool IsCloudOnlyFile(FileAttributes fileAttributes);

    /// <summary>
    /// Determines if a file is locally available (fully downloaded)
    /// </summary>
    /// <param name="fileAttributes">The file attributes from Windows</param>
    /// <returns>True if the file is locally available, false otherwise</returns>
    bool IsLocallyAvailable(FileAttributes fileAttributes);
}
