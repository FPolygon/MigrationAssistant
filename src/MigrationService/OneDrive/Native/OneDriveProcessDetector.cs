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
    private const string OneDriveProcessName = "OneDrive";

    public OneDriveProcessDetector(ILogger<OneDriveProcessDetector> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if OneDrive is running for any user
    /// </summary>
    public virtual bool IsOneDriveRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName(OneDriveProcessName);
            return processes.Length > 0;
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
        return await Task.Run(() =>
        {
            try
            {
                // Use WMI to get process owner information
                var query = $"SELECT ProcessId, Name FROM Win32_Process WHERE Name = '{OneDriveProcessName}.exe'";
                using var searcher = new ManagementObjectSearcher(query);
                using var results = searcher.Get();

                foreach (ManagementObject process in results)
                {
                    try
                    {
                        var processId = Convert.ToInt32(process["ProcessId"]);
                        var ownerSid = GetProcessOwnerSid(processId);

                        if (ownerSid != null && ownerSid.Equals(userSid, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Found OneDrive process {ProcessId} for user {Sid}",
                                processId, userSid);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check process owner");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check OneDrive process for user {Sid}", userSid);
            }

            return false;
        });
    }

    /// <summary>
    /// Gets all OneDrive process IDs
    /// </summary>
    public virtual List<int> GetOneDriveProcessIds()
    {
        var processIds = new List<int>();

        try
        {
            var processes = Process.GetProcessesByName(OneDriveProcessName);
            processIds.AddRange(processes.Select(p => p.Id));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get OneDrive process IDs");
        }

        return processIds;
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

    #region Private Methods

    private string? GetProcessOwnerSid(int processId)
    {
        try
        {
            var query = $"SELECT * FROM Win32_Process WHERE ProcessId = {processId}";
            using var searcher = new ManagementObjectSearcher(query);
            using var results = searcher.Get();

            foreach (ManagementObject process in results)
            {
                var argList = new string[] { string.Empty, string.Empty };
                var returnVal = Convert.ToInt32(process.InvokeMethod("GetOwner", argList));

                if (returnVal == 0)
                {
                    var owner = argList[1] + "\\" + argList[0]; // DOMAIN\Username

                    // Convert to SID
                    try
                    {
                        var account = new NTAccount(owner);
                        var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                        return sid.Value;
                    }
                    catch
                    {
                        // Failed to translate to SID
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get process owner for PID {ProcessId}", processId);
        }

        return null;
    }

    #endregion
}
