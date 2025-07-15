using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Windows-specific implementation of OneDrive file attribute service
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveAttributeService : IOneDriveAttributeService
{
    private readonly ILogger<OneDriveAttributeService> _logger;

    // OneDrive file attribute constants
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000; // FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000; // FILE_ATTRIBUTE_RECALL_ON_OPEN
    private const FileAttributes Pinned = (FileAttributes)0x00080000; // FILE_ATTRIBUTE_PINNED
    private const FileAttributes Unpinned = (FileAttributes)0x00100000; // FILE_ATTRIBUTE_UNPINNED

    public OneDriveAttributeService(ILogger<OneDriveAttributeService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public FileSyncState GetFileSyncState(FileAttributes fileAttributes)
    {
        try
        {
            // Check if it's a placeholder (cloud-only) file
            if (IsCloudOnlyFile(fileAttributes))
            {
                return FileSyncState.CloudOnly;
            }

            // Check if it's pinned (always available offline)
            if (IsFilePinned(fileAttributes))
            {
                return FileSyncState.LocallyAvailable;
            }

            // File is in sync folder and not a placeholder
            return FileSyncState.InSync;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine sync state from file attributes: {Attributes}", fileAttributes);
            return FileSyncState.Unknown;
        }
    }

    /// <inheritdoc/>
    public bool IsFilePinned(FileAttributes fileAttributes)
    {
        return (fileAttributes & Pinned) != 0;
    }

    /// <inheritdoc/>
    public bool IsCloudOnlyFile(FileAttributes fileAttributes)
    {
        return (fileAttributes & RecallOnDataAccess) != 0 || (fileAttributes & RecallOnOpen) != 0;
    }

    /// <inheritdoc/>
    public bool IsLocallyAvailable(FileAttributes fileAttributes)
    {
        // A file is locally available if it's not a cloud-only placeholder
        return !IsCloudOnlyFile(fileAttributes);
    }
}