using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Interface for detecting and monitoring OneDrive processes
/// </summary>
[SupportedOSPlatform("windows")]
public interface IOneDriveProcessDetector
{
    /// <summary>
    /// Checks if OneDrive is running for any user
    /// </summary>
    /// <returns>True if OneDrive is running</returns>
    bool IsOneDriveRunning();

    /// <summary>
    /// Checks if OneDrive is running for a specific user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <returns>True if OneDrive is running for the specified user</returns>
    Task<bool> IsOneDriveRunningForUserAsync(string userSid);

    /// <summary>
    /// Gets all OneDrive process IDs
    /// </summary>
    /// <returns>List of process IDs for OneDrive processes</returns>
    List<int> GetOneDriveProcessIds();

    /// <summary>
    /// Attempts to start OneDrive for a user
    /// </summary>
    /// <param name="userSid">User's security identifier</param>
    /// <param name="oneDrivePath">Path to OneDrive executable</param>
    /// <returns>True if OneDrive was started successfully</returns>
    Task<bool> StartOneDriveForUserAsync(string userSid, string oneDrivePath);

    /// <summary>
    /// Gets process start time if available
    /// </summary>
    /// <param name="processId">Process ID</param>
    /// <returns>Process start time or null if not available</returns>
    DateTime? GetProcessStartTime(int processId);
}
