using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MigrationTool.Service.ProfileManagement.Native;

/// <summary>
/// Provides low-level access to Windows profile information via the Registry
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProfileRegistry : IWindowsProfileRegistry
{
    private readonly ILogger<WindowsProfileRegistry> _logger;
    private const string ProfileListKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    private const string ProfilesDirectory = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\ProfilesDirectory";

    // Well-known system SIDs to filter out
    private static readonly HashSet<string> SystemSids = new()
    {
        "S-1-5-18", // LOCAL_SYSTEM
        "S-1-5-19", // LOCAL_SERVICE
        "S-1-5-20", // NETWORK_SERVICE
        "S-1-5-80", // Service accounts prefix
        "S-1-5-82", // IIS AppPool prefix
        "S-1-5-83", // Virtual Machine accounts
        "S-1-5-90", // Windows Manager prefix
        "S-1-5-96", // Font Driver Host prefix
    };

    public WindowsProfileRegistry(ILogger<WindowsProfileRegistry> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Enumerates all user profiles from the Windows Registry
    /// </summary>
    public List<ProfileRegistryInfo> EnumerateProfiles()
    {
        var profiles = new List<ProfileRegistryInfo>();

        try
        {
            using var profileListKey = Registry.LocalMachine.OpenSubKey(ProfileListKey);
            if (profileListKey == null)
            {
                _logger.LogError("Unable to open ProfileList registry key");
                return profiles;
            }

            // Get the default profiles directory
            var profilesDirectory = profileListKey.GetValue("ProfilesDirectory") as string ?? @"C:\Users";

            // Enumerate all subkeys (SIDs)
            foreach (var sidString in profileListKey.GetSubKeyNames())
            {
                // Skip non-SID entries
                if (!sidString.StartsWith("S-1-5-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var profile = ReadProfileInfo(profileListKey, sidString, profilesDirectory);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to read profile information for SID: {Sid}", sidString);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate user profiles from registry");
        }

        return profiles;
    }

    /// <summary>
    /// Reads detailed information about a specific profile
    /// </summary>
    private ProfileRegistryInfo? ReadProfileInfo(RegistryKey profileListKey, string sid, string defaultProfilesPath)
    {
        using var profileKey = profileListKey.OpenSubKey(sid);
        if (profileKey == null)
        {
            return null;
        }

        var profilePath = profileKey.GetValue("ProfileImagePath") as string;
        if (string.IsNullOrEmpty(profilePath))
        {
            return null;
        }

        // Expand environment variables in the path
        profilePath = Environment.ExpandEnvironmentVariables(profilePath);

        var info = new ProfileRegistryInfo
        {
            Sid = sid,
            ProfilePath = profilePath,
            IsSystemAccount = IsSystemAccount(sid),
            State = (ProfileState)(profileKey.GetValue("State") ?? 0),
            Flags = (ProfileFlags)(profileKey.GetValue("Flags") ?? 0),
        };

        // Try to get the username from the profile path
        if (!string.IsNullOrEmpty(profilePath))
        {
            info.UserName = Path.GetFileName(profilePath);
        }

        // Additional profile metadata
        if (profileKey.GetValue("Guid") is string guid)
        {
            info.Guid = guid;
        }

        // Check if this is a temporary profile
        info.IsTemporary = profilePath.EndsWith(".TEMP", StringComparison.OrdinalIgnoreCase) ||
                          profilePath.EndsWith(".TMP", StringComparison.OrdinalIgnoreCase) ||
                          (info.State & ProfileState.Temporary) != 0;

        // Check if profile is mandatory (roaming)
        info.IsMandatory = (info.State & ProfileState.Mandatory) != 0;

        // Check if profile is corrupted
        info.IsCorrupted = (info.State & ProfileState.Corrupted) != 0 ||
                          !Directory.Exists(profilePath);

        // Get profile load/unload times if available
        var loadTimeHigh = profileKey.GetValue("LocalProfileLoadTimeHigh") as int?;
        var loadTimeLow = profileKey.GetValue("LocalProfileLoadTimeLow") as int?;
        if (loadTimeHigh.HasValue && loadTimeLow.HasValue)
        {
            info.LastLoadTime = FileTimeToDateTime(loadTimeHigh.Value, loadTimeLow.Value);
        }

        var unloadTimeHigh = profileKey.GetValue("LocalProfileUnloadTimeHigh") as int?;
        var unloadTimeLow = profileKey.GetValue("LocalProfileUnloadTimeLow") as int?;
        if (unloadTimeHigh.HasValue && unloadTimeLow.HasValue)
        {
            info.LastUnloadTime = FileTimeToDateTime(unloadTimeHigh.Value, unloadTimeLow.Value);
        }

        return info;
    }

    /// <summary>
    /// Determines if a SID represents a system account
    /// </summary>
    public bool IsSystemAccount(string sid)
    {
        // Check exact matches
        if (SystemSids.Contains(sid))
        {
            return true;
        }

        // Check prefixes for service accounts
        foreach (var systemPrefix in SystemSids)
        {
            if (sid.StartsWith(systemPrefix + "-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check for well-known RIDs
        if (sid.EndsWith("-500") || // Administrator
            sid.EndsWith("-501") || // Guest
            sid.EndsWith("-502") || // KRBTGT
            sid.EndsWith("-503"))   // DefaultAccount
        {
            return false; // These are user accounts, not system accounts
        }

        return false;
    }

    /// <summary>
    /// Gets the account type from a SID
    /// </summary>
    public ProfileAccountType GetAccountType(string sid, string? userName = null)
    {
        try
        {
            // Check for Azure AD accounts (have specific SID pattern)
            if (sid.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase))
            {
                return ProfileAccountType.AzureAD;
            }

            // Try to create SecurityIdentifier to check domain
            var securityId = new SecurityIdentifier(sid);

            // Check if it's a built-in account
            if (securityId.IsWellKnown(WellKnownSidType.BuiltinDomainSid) ||
                securityId.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                securityId.IsWellKnown(WellKnownSidType.LocalServiceSid) ||
                securityId.IsWellKnown(WellKnownSidType.NetworkServiceSid))
            {
                return ProfileAccountType.System;
            }

            // Check if the account name contains domain information
            if (!string.IsNullOrEmpty(userName) && userName.Contains('\\'))
            {
                return ProfileAccountType.Domain;
            }

            // Check if SID indicates domain account (not S-1-5-21-...-500/501/502/503/1000+)
            if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
            {
                var parts = sid.Split('-');
                if (parts.Length > 7)
                {
                    if (int.TryParse(parts[^1], out var rid))
                    {
                        // RIDs < 1000 are typically built-in accounts
                        // RIDs >= 1000 are typically user accounts
                        // Domain SIDs have 4 sub-authorities after S-1-5-21
                        return rid >= 1000 ? ProfileAccountType.Local : ProfileAccountType.System;
                    }
                }
            }

            return ProfileAccountType.Local;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to determine account type for SID: {Sid}", sid);
            return ProfileAccountType.Unknown;
        }
    }

    /// <summary>
    /// Converts Windows FILETIME to DateTime
    /// </summary>
    private static DateTime? FileTimeToDateTime(int high, int low)
    {
        try
        {
            long fileTime = ((long)high << 32) | (uint)low;
            if (fileTime == 0)
            {
                return null;
            }

            return DateTime.FromFileTimeUtc(fileTime);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Raw profile information from the Registry
/// </summary>
public class ProfileRegistryInfo
{
    public string Sid { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public string? Guid { get; set; }
    public bool IsSystemAccount { get; set; }
    public bool IsTemporary { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsCorrupted { get; set; }
    public ProfileState State { get; set; }
    public ProfileFlags Flags { get; set; }
    public DateTime? LastLoadTime { get; set; }
    public DateTime? LastUnloadTime { get; set; }
}

/// <summary>
/// Profile state flags from Registry
/// </summary>
[Flags]
public enum ProfileState
{
    None = 0,
    Mandatory = 0x1,
    Temporary = 0x4,
    Corrupted = 0x8,
    New = 0x10,
    Loaded = 0x20,
    Default = 0x80
}

/// <summary>
/// Profile flags from Registry
/// </summary>
[Flags]
public enum ProfileFlags
{
    None = 0,
    GuestUser = 0x1,
    AdminUser = 0x2,
    MandatoryProfile = 0x4,
    TemporaryProfile = 0x8
}

/// <summary>
/// Type of Windows account
/// </summary>
public enum ProfileAccountType
{
    Unknown,
    Local,
    Domain,
    AzureAD,
    System
}
