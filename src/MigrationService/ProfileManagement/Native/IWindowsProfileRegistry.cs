using System.Runtime.Versioning;

namespace MigrationTool.Service.ProfileManagement.Native;

/// <summary>
/// Interface for accessing Windows profile information via the Registry
/// </summary>
[SupportedOSPlatform("windows")]
public interface IWindowsProfileRegistry
{
    /// <summary>
    /// Enumerates all user profiles from the Windows Registry
    /// </summary>
    List<ProfileRegistryInfo> EnumerateProfiles();

    /// <summary>
    /// Determines if a SID represents a system account
    /// </summary>
    bool IsSystemAccount(string sid);

    /// <summary>
    /// Gets the account type from a SID
    /// </summary>
    ProfileAccountType GetAccountType(string sid, string? userName = null);
}