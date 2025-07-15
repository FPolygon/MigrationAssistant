using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Interface for accessing OneDrive configuration from Windows Registry
/// </summary>
[SupportedOSPlatform("windows")]
public interface IOneDriveRegistry
{
    /// <summary>
    /// Checks if OneDrive is installed on the system
    /// </summary>
    /// <returns>True if OneDrive is installed</returns>
    bool IsOneDriveInstalled();

    /// <summary>
    /// Gets the OneDrive installation path
    /// </summary>
    /// <returns>Path to OneDrive.exe or null if not found</returns>
    string? GetOneDriveInstallPath();

    /// <summary>
    /// Gets OneDrive account information for a specific user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="userRegistryHive">Optional pre-loaded user registry hive</param>
    /// <returns>List of OneDrive accounts for the user</returns>
    Task<List<OneDriveAccountInfo>> GetUserAccountsAsync(string userSid, Microsoft.Win32.RegistryKey? userRegistryHive = null);

    /// <summary>
    /// Gets the Known Folder Move status for a user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="userRegistryHive">Optional pre-loaded user registry hive</param>
    /// <returns>KFM status or null if not configured</returns>
    Task<KnownFolderMoveStatus?> GetKnownFolderMoveStatusAsync(string userSid, Microsoft.Win32.RegistryKey? userRegistryHive = null);

    /// <summary>
    /// Gets all synced folders for a user including SharePoint libraries
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="userRegistryHive">Optional pre-loaded user registry hive</param>
    /// <returns>List of all synced folders</returns>
    Task<List<OneDriveSyncFolder>> GetSyncedFoldersAsync(string userSid, Microsoft.Win32.RegistryKey? userRegistryHive = null);

    /// <summary>
    /// Checks if OneDrive sync is paused
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="userRegistryHive">Optional pre-loaded user registry hive</param>
    /// <returns>True if sync is paused</returns>
    Task<bool> IsSyncPausedAsync(string userSid, Microsoft.Win32.RegistryKey? userRegistryHive = null);

    /// <summary>
    /// Gets the primary business account ID (usually "Business1")
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="userRegistryHive">Optional pre-loaded user registry hive</param>
    /// <returns>Primary account ID or null if not found</returns>
    Task<string?> GetPrimaryBusinessAccountIdAsync(string userSid, Microsoft.Win32.RegistryKey? userRegistryHive = null);

    /// <summary>
    /// Loads a user's registry hive for access
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <returns>Registry key for the user's hive</returns>
    Task<Microsoft.Win32.RegistryKey?> LoadUserRegistryHiveAsync(string userSid);
}
