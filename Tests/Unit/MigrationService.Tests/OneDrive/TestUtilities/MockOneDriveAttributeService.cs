using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Models;
using MigrationTool.Service.OneDrive.Native;

namespace MigrationService.Tests.OneDrive.TestUtilities;

/// <summary>
/// Mock implementation of OneDrive attribute service for testing
/// </summary>
[SupportedOSPlatform("windows")]
public class MockOneDriveAttributeService : IOneDriveAttributeService
{
    private readonly Dictionary<FileAttributes, FileSyncState> _attributeToStateMap = new();
    private readonly Dictionary<FileAttributes, bool> _attributeToPinnedMap = new();

    /// <summary>
    /// Configures the mock to return a specific sync state for given attributes
    /// </summary>
    public void SetSyncState(FileAttributes attributes, FileSyncState state)
    {
        _attributeToStateMap[attributes] = state;
    }

    /// <summary>
    /// Configures the mock to return a specific pinned state for given attributes
    /// </summary>
    public void SetPinnedState(FileAttributes attributes, bool isPinned)
    {
        _attributeToPinnedMap[attributes] = isPinned;
    }

    /// <summary>
    /// Sets up common OneDrive attribute mappings for testing
    /// </summary>
    public void SetupCommonMappings()
    {
        // Normal file attributes (no OneDrive attributes)
        SetSyncState(FileAttributes.Normal, FileSyncState.LocalOnly);
        SetPinnedState(FileAttributes.Normal, false);

        // Cloud-only file (RecallOnDataAccess)
        var cloudOnlyAttributes = FileAttributes.Normal | (FileAttributes)0x00400000;
        SetSyncState(cloudOnlyAttributes, FileSyncState.CloudOnly);
        SetPinnedState(cloudOnlyAttributes, false);

        // Pinned file (LocallyAvailable)
        var pinnedAttributes = FileAttributes.Normal | (FileAttributes)0x00080000;
        SetSyncState(pinnedAttributes, FileSyncState.LocallyAvailable);
        SetPinnedState(pinnedAttributes, true);

        // In-sync file (normal file in OneDrive folder)
        SetSyncState(FileAttributes.Normal, FileSyncState.InSync);
    }

    /// <inheritdoc/>
    public FileSyncState GetFileSyncState(FileAttributes fileAttributes)
    {
        if (_attributeToStateMap.TryGetValue(fileAttributes, out var state))
        {
            return state;
        }

        // Default behavior for unmapped attributes
        return FileSyncState.Unknown;
    }

    /// <inheritdoc/>
    public bool IsFilePinned(FileAttributes fileAttributes)
    {
        if (_attributeToPinnedMap.TryGetValue(fileAttributes, out var isPinned))
        {
            return isPinned;
        }

        // Default behavior
        return false;
    }

    /// <inheritdoc/>
    public bool IsCloudOnlyFile(FileAttributes fileAttributes)
    {
        // Check for RecallOnDataAccess or RecallOnOpen attributes
        const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
        const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
        
        return (fileAttributes & RecallOnDataAccess) != 0 || (fileAttributes & RecallOnOpen) != 0;
    }

    /// <inheritdoc/>
    public bool IsLocallyAvailable(FileAttributes fileAttributes)
    {
        return !IsCloudOnlyFile(fileAttributes);
    }
}