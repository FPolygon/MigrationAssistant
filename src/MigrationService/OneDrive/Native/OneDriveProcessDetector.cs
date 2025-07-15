using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Detects and monitors OneDrive processes
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveProcessDetector : IOneDriveProcessDetector
{
    private readonly ILogger<OneDriveProcessDetector> _logger;
    private readonly IProcessService _processService;
    private const string OneDriveProcessName = "OneDrive";

    public OneDriveProcessDetector(ILogger<OneDriveProcessDetector> logger, IProcessService processService)
    {
        _logger = logger;
        _processService = processService;
    }

    /// <summary>
    /// Checks if OneDrive is running for any user
    /// </summary>
    public virtual bool IsOneDriveRunning()
    {
        try
        {
            return _processService.IsProcessRunningAsync(OneDriveProcessName).Result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if OneDrive is running");
            return false;
        }
    }

    /// <summary>
    /// Checks if OneDrive is running for a specific user
    /// </summary>
    public virtual async Task<bool> IsOneDriveRunningForUserAsync(string userSid)
    {
        try
        {
            return await _processService.IsProcessRunningForUserAsync(OneDriveProcessName, userSid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check OneDrive process for user {Sid}", userSid);
            return false;
        }
    }

    /// <summary>
    /// Gets all OneDrive process IDs
    /// </summary>
    public virtual List<int> GetOneDriveProcessIds()
    {
        try
        {
            return _processService.GetProcessIdsByNameAsync(OneDriveProcessName).Result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OneDrive process IDs");
            return new List<int>();
        }
    }

    /// <summary>
    /// Attempts to start OneDrive for a user
    /// </summary>
    public virtual Task<bool> StartOneDriveForUserAsync(string userSid, string oneDrivePath)
    {
        try
        {
            // This would typically require user impersonation or running in user context
            // For now, we'll return false as the service runs as SYSTEM
            _logger.LogInformation("Starting OneDrive requires user context. " +
                "This should be initiated through the agent in Phase 4.");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start OneDrive for user {Sid}", userSid);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Gets process start time if available
    /// </summary>
    public virtual DateTime? GetProcessStartTime(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.StartTime;
        }
        catch
        {
            return null;
        }
    }

}
