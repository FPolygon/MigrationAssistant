using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using MigrationTool.Service.OneDrive.Models;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Provides access to OneDrive configuration via Windows Registry
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveRegistry : IOneDriveRegistry
{
    private readonly ILogger<OneDriveRegistry> _logger;

    // Registry paths
    private const string OneDriveMachineKey = @"SOFTWARE\Microsoft\OneDrive";
    private const string OneDriveUserKey = @"Software\Microsoft\OneDrive";
    private const string AccountsSubKey = @"Accounts";
    private const string KnownFolderMoveKey = @"Software\Microsoft\OneDrive\Accounts\{0}\KnownFolderMove";
    private const string SyncEngineKey = @"Software\SyncEngines\Providers OneDrive";

    // Common registry value names
    private const string UserFolderValue = "UserFolder";
    private const string DisplayNameValue = "DisplayName";
    private const string EmailValue = "UserEmail";
    private const string ServiceEndpointValue = "ServiceEndpointUri";
    private const string CidValue = "cid";

    public OneDriveRegistry(ILogger<OneDriveRegistry> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public bool IsOneDriveInstalled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(OneDriveMachineKey);
            if (key != null)
            {
                var installPath = GetOneDriveInstallPath();
                return !string.IsNullOrEmpty(installPath) && File.Exists(installPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check OneDrive installation");
        }

        return false;
    }

    /// <inheritdoc/>
    public string? GetOneDriveInstallPath()
    {
        try
        {
            // Try current user first
            using (var key = Registry.CurrentUser.OpenSubKey(OneDriveUserKey))
            {
                if (key != null)
                {
                    var updatePath = key.GetValue("OneDriveTrigger") as string;
                    if (!string.IsNullOrEmpty(updatePath) && File.Exists(updatePath))
                    {
                        return updatePath;
                    }
                }
            }

            // Try local machine
            using (var key = Registry.LocalMachine.OpenSubKey(OneDriveMachineKey))
            {
                if (key != null)
                {
                    var installDir = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrEmpty(installDir))
                    {
                        var exePath = Path.Combine(installDir, "OneDrive.exe");
                        if (File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                }
            }

            // Try common installation paths
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "OneDrive", "OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft OneDrive", "OneDrive.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Microsoft OneDrive", "OneDrive.exe")
            };

            return commonPaths.FirstOrDefault(File.Exists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive install path");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<List<OneDriveAccountInfo>> GetUserAccountsAsync(string userSid, RegistryKey? userRegistryHive = null)
    {
        var accounts = new List<OneDriveAccountInfo>();

        try
        {
            var hive = userRegistryHive ?? await LoadUserRegistryHiveAsync(userSid);
            if (hive == null)
            {
                _logger.LogWarning("Unable to load registry hive for user {Sid}", userSid);
                return accounts;
            }

            try
            {
                using var oneDriveKey = hive.OpenSubKey(OneDriveUserKey);
                if (oneDriveKey == null)
                {
                    _logger.LogDebug("OneDrive registry key not found for user {Sid}", userSid);
                    return accounts;
                }

                using var accountsKey = oneDriveKey.OpenSubKey(AccountsSubKey);
                if (accountsKey == null)
                {
                    _logger.LogDebug("No OneDrive accounts found for user {Sid}", userSid);
                    return accounts;
                }

                // Enumerate all account subkeys
                foreach (var accountName in accountsKey.GetSubKeyNames())
                {
                    try
                    {
                        // Focus on Business accounts only
                        if (!accountName.StartsWith("Business", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        using var accountKey = accountsKey.OpenSubKey(accountName);
                        if (accountKey == null)
                        {
                            continue;
                        }

                        var account = new OneDriveAccountInfo
                        {
                            AccountId = accountName,
                            IsPrimary = accountName.Equals("Business1", StringComparison.OrdinalIgnoreCase),
                            UserFolder = accountKey.GetValue(UserFolderValue) as string ?? string.Empty,
                            Email = accountKey.GetValue(EmailValue) as string ?? string.Empty,
                            DisplayName = accountKey.GetValue(DisplayNameValue) as string
                        };

                        // Only add if we have minimum required information
                        if (!string.IsNullOrEmpty(account.UserFolder) && !string.IsNullOrEmpty(account.Email))
                        {
                            // Expand environment variables in the path
                            account.UserFolder = Environment.ExpandEnvironmentVariables(account.UserFolder);

                            // Get KFM status for this account
                            account.KfmStatus = await GetKnownFolderMoveStatusForAccountAsync(
                                userSid, accountName, hive);

                            accounts.Add(account);
                            _logger.LogDebug("Found OneDrive account {AccountId} for user {Sid}",
                                accountName, userSid);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to read account {AccountName} for user {Sid}",
                            accountName, userSid);
                    }
                }
            }
            finally
            {
                // Dispose of loaded hive if we created it
                if (userRegistryHive == null && hive != null)
                {
                    hive.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive accounts for user {Sid}", userSid);
        }

        return accounts;
    }

    /// <inheritdoc/>
    public async Task<KnownFolderMoveStatus?> GetKnownFolderMoveStatusAsync(string userSid, RegistryKey? userRegistryHive = null)
    {
        // Get primary business account first
        var primaryAccountId = await GetPrimaryBusinessAccountIdAsync(userSid, userRegistryHive);
        if (string.IsNullOrEmpty(primaryAccountId))
        {
            return null;
        }

        return await GetKnownFolderMoveStatusForAccountAsync(userSid, primaryAccountId, userRegistryHive);
    }

    /// <inheritdoc/>
    public async Task<List<OneDriveSyncFolder>> GetSyncedFoldersAsync(string userSid, RegistryKey? userRegistryHive = null)
    {
        var syncedFolders = new List<OneDriveSyncFolder>();

        try
        {
            var hive = userRegistryHive ?? await LoadUserRegistryHiveAsync(userSid);
            if (hive == null)
            {
                return syncedFolders;
            }

            try
            {
                // Get all OneDrive accounts first
                var accounts = await GetUserAccountsAsync(userSid, hive);

                foreach (var account in accounts)
                {
                    // Add the main OneDrive folder
                    if (!string.IsNullOrEmpty(account.UserFolder) && Directory.Exists(account.UserFolder))
                    {
                        syncedFolders.Add(new OneDriveSyncFolder
                        {
                            LocalPath = account.UserFolder,
                            FolderType = SyncFolderType.Business,
                            DisplayName = $"OneDrive - {account.DisplayName ?? account.Email}"
                        });
                    }

                    // Add KFM folders if configured
                    if (account.KfmStatus != null)
                    {
                        if (account.KfmStatus.DesktopRedirected && !string.IsNullOrEmpty(account.KfmStatus.DesktopPath))
                        {
                            syncedFolders.Add(new OneDriveSyncFolder
                            {
                                LocalPath = account.KfmStatus.DesktopPath,
                                FolderType = SyncFolderType.KnownFolder,
                                DisplayName = "Desktop"
                            });
                        }

                        if (account.KfmStatus.DocumentsRedirected && !string.IsNullOrEmpty(account.KfmStatus.DocumentsPath))
                        {
                            syncedFolders.Add(new OneDriveSyncFolder
                            {
                                LocalPath = account.KfmStatus.DocumentsPath,
                                FolderType = SyncFolderType.KnownFolder,
                                DisplayName = "Documents"
                            });
                        }

                        if (account.KfmStatus.PicturesRedirected && !string.IsNullOrEmpty(account.KfmStatus.PicturesPath))
                        {
                            syncedFolders.Add(new OneDriveSyncFolder
                            {
                                LocalPath = account.KfmStatus.PicturesPath,
                                FolderType = SyncFolderType.KnownFolder,
                                DisplayName = "Pictures"
                            });
                        }
                    }
                }

                // Check for SharePoint sync folders
                var sharePointFolders = await GetSharePointSyncFoldersAsync(userSid, hive);
                syncedFolders.AddRange(sharePointFolders);
            }
            finally
            {
                if (userRegistryHive == null && hive != null)
                {
                    hive.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get synced folders for user {Sid}", userSid);
        }

        return syncedFolders;
    }

    /// <inheritdoc/>
    public async Task<bool> IsSyncPausedAsync(string userSid, RegistryKey? userRegistryHive = null)
    {
        try
        {
            var hive = userRegistryHive ?? await LoadUserRegistryHiveAsync(userSid);
            if (hive == null)
            {
                return false;
            }

            try
            {
                using var oneDriveKey = hive.OpenSubKey(OneDriveUserKey);
                if (oneDriveKey != null)
                {
                    var pausedValue = oneDriveKey.GetValue("IsPaused");
                    if (pausedValue is int paused)
                    {
                        return paused == 1;
                    }
                }
            }
            finally
            {
                if (userRegistryHive == null && hive != null)
                {
                    hive.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check sync pause status for user {Sid}", userSid);
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task<string?> GetPrimaryBusinessAccountIdAsync(string userSid, RegistryKey? userRegistryHive = null)
    {
        try
        {
            var accounts = await GetUserAccountsAsync(userSid, userRegistryHive);

            // Look for Business1 first (primary account)
            var primaryAccount = accounts.FirstOrDefault(a => a.IsPrimary);
            if (primaryAccount != null)
            {
                return primaryAccount.AccountId;
            }

            // If no Business1, return the first business account
            return accounts.FirstOrDefault()?.AccountId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get primary business account for user {Sid}", userSid);
            return null;
        }
    }

    /// <inheritdoc/>
    public virtual async Task<RegistryKey?> LoadUserRegistryHiveAsync(string userSid)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Try to open the user's registry hive
                var sid = new SecurityIdentifier(userSid);

                // First try HKEY_USERS\SID
                var userKey = Registry.Users.OpenSubKey(userSid);
                if (userKey != null)
                {
                    return userKey;
                }

                // If running as SYSTEM, we might need to load the hive manually
                // This would require additional P/Invoke calls to RegLoadKey
                _logger.LogWarning("Unable to directly access registry for user {Sid}. " +
                    "User may not be logged in or hive not loaded.", userSid);

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load user registry hive for {Sid}", userSid);
                return null;
            }
        });
    }

    #region Private Helper Methods

    private async Task<KnownFolderMoveStatus?> GetKnownFolderMoveStatusForAccountAsync(
        string userSid, string accountId, RegistryKey? userRegistryHive)
    {
        try
        {
            var hive = userRegistryHive ?? await LoadUserRegistryHiveAsync(userSid);
            if (hive == null)
            {
                return null;
            }

            try
            {
                var kfmKeyPath = string.Format(KnownFolderMoveKey, accountId);
                using var kfmKey = hive.OpenSubKey(kfmKeyPath);

                if (kfmKey == null)
                {
                    _logger.LogDebug("No KFM configuration found for account {AccountId}", accountId);
                    return null;
                }

                var status = new KnownFolderMoveStatus
                {
                    ConfigurationSource = kfmKeyPath
                };

                // Check each known folder
                var desktopValue = kfmKey.GetValue("Desktop") as string;
                if (!string.IsNullOrEmpty(desktopValue))
                {
                    status.DesktopRedirected = true;
                    status.DesktopPath = Environment.ExpandEnvironmentVariables(desktopValue);
                }

                var documentsValue = kfmKey.GetValue("Documents") as string;
                if (!string.IsNullOrEmpty(documentsValue))
                {
                    status.DocumentsRedirected = true;
                    status.DocumentsPath = Environment.ExpandEnvironmentVariables(documentsValue);
                }

                var picturesValue = kfmKey.GetValue("Pictures") as string;
                if (!string.IsNullOrEmpty(picturesValue))
                {
                    status.PicturesRedirected = true;
                    status.PicturesPath = Environment.ExpandEnvironmentVariables(picturesValue);
                }

                status.IsEnabled = status.RedirectedFolderCount > 0;

                return status;
            }
            finally
            {
                if (userRegistryHive == null && hive != null)
                {
                    hive.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get KFM status for account {AccountId}", accountId);
            return null;
        }
    }

    private Task<List<OneDriveSyncFolder>> GetSharePointSyncFoldersAsync(string userSid, RegistryKey hive)
    {
        var folders = new List<OneDriveSyncFolder>();

        try
        {
            // SharePoint sync folders are typically registered under SyncEngines
            using var syncEngineKey = hive.OpenSubKey(SyncEngineKey);
            if (syncEngineKey == null)
            {
                return Task.FromResult(folders);
            }

            foreach (var mountPoint in syncEngineKey.GetSubKeyNames())
            {
                try
                {
                    using var mountKey = syncEngineKey.OpenSubKey(mountPoint);
                    if (mountKey == null)
                    {
                        continue;
                    }

                    var urlNamespace = mountKey.GetValue("UrlNamespace") as string;
                    var mountPointPath = mountKey.GetValue("MountPoint") as string;

                    if (!string.IsNullOrEmpty(mountPointPath) && !string.IsNullOrEmpty(urlNamespace))
                    {
                        // Determine if this is a SharePoint library
                        if (urlNamespace.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
                            urlNamespace.Contains("-my.sharepoint.com", StringComparison.OrdinalIgnoreCase))
                        {
                            var expandedPath = Environment.ExpandEnvironmentVariables(mountPointPath);

                            folders.Add(new OneDriveSyncFolder
                            {
                                LocalPath = expandedPath,
                                RemotePath = urlNamespace,
                                FolderType = SyncFolderType.SharePointLibrary,
                                DisplayName = Path.GetFileName(expandedPath),
                                SharePointSiteUrl = ExtractSharePointUrl(urlNamespace)
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read sync engine mount point {MountPoint}", mountPoint);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate SharePoint sync folders for user {Sid}", userSid);
        }

        return Task.FromResult(folders);
    }

    private string? ExtractSharePointUrl(string urlNamespace)
    {
        try
        {
            if (Uri.TryCreate(urlNamespace, UriKind.Absolute, out var uri))
            {
                return $"{uri.Scheme}://{uri.Host}";
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    #endregion
}
