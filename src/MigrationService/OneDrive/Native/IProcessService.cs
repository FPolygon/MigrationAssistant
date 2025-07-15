using System.Runtime.Versioning;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Abstraction for process detection operations to improve testability
/// </summary>
[SupportedOSPlatform("windows")]
public interface IProcessService
{
    /// <summary>
    /// Gets all processes with the specified name
    /// </summary>
    /// <param name="processName">The name of the process to find</param>
    /// <returns>Array of process information</returns>
    Task<ProcessInfo[]> GetProcessesByNameAsync(string processName);

    /// <summary>
    /// Gets the owner SID for a specific process
    /// </summary>
    /// <param name="processId">The process ID</param>
    /// <returns>The owner SID or null if not found</returns>
    Task<string?> GetProcessOwnerSidAsync(int processId);

    /// <summary>
    /// Checks if any process with the specified name is running
    /// </summary>
    /// <param name="processName">The name of the process to check</param>
    /// <returns>True if the process is running, false otherwise</returns>
    Task<bool> IsProcessRunningAsync(string processName);

    /// <summary>
    /// Checks if a process with the specified name is running for a specific user
    /// </summary>
    /// <param name="processName">The name of the process to check</param>
    /// <param name="userSid">The user SID to check for</param>
    /// <returns>True if the process is running for the user, false otherwise</returns>
    Task<bool> IsProcessRunningForUserAsync(string processName, string userSid);

    /// <summary>
    /// Gets all process IDs for processes with the specified name
    /// </summary>
    /// <param name="processName">The name of the process to find</param>
    /// <returns>List of process IDs</returns>
    Task<List<int>> GetProcessIdsByNameAsync(string processName);
}

/// <summary>
/// Process information for abstraction
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string? OwnerSid { get; set; }
}