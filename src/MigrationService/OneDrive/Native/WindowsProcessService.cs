using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.OneDrive.Native;

/// <summary>
/// Windows-specific implementation of process detection operations
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsProcessService : IProcessService
{
    private readonly ILogger<WindowsProcessService> _logger;

    public WindowsProcessService(ILogger<WindowsProcessService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ProcessInfo[]> GetProcessesByNameAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return Array.Empty<ProcessInfo>();
        }

        return await Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                var processInfos = new List<ProcessInfo>();

                foreach (var process in processes)
                {
                    try
                    {
                        var ownerSid = GetProcessOwnerSid(process.Id);
                        processInfos.Add(new ProcessInfo
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            OwnerSid = ownerSid
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to get owner SID for process {ProcessId}", process.Id);
                        processInfos.Add(new ProcessInfo
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            OwnerSid = null
                        });
                    }
                }

                return processInfos.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get processes by name: {ProcessName}", processName);
                return Array.Empty<ProcessInfo>();
            }
        });
    }

    /// <inheritdoc/>
    public async Task<string?> GetProcessOwnerSidAsync(int processId)
    {
        return await Task.Run(() => GetProcessOwnerSid(processId));
    }

    /// <inheritdoc/>
    public async Task<bool> IsProcessRunningAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check if process is running: {ProcessName}", processName);
                return false;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<bool> IsProcessRunningForUserAsync(string processName, string userSid)
    {
        if (string.IsNullOrWhiteSpace(processName) || string.IsNullOrWhiteSpace(userSid))
        {
            return false;
        }

        return await Task.Run(() =>
        {
            try
            {
                // Use WMI to get process owner information
                var query = $"SELECT ProcessId, Name FROM Win32_Process WHERE Name = '{processName}.exe'";
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
                            _logger.LogDebug("Found {ProcessName} process {ProcessId} for user {Sid}",
                                processName, processId, userSid);
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check process owner for process ID");
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to check if {ProcessName} is running for user {Sid}", processName, userSid);
                return false;
            }
        });
    }

    /// <inheritdoc/>
    public async Task<List<int>> GetProcessIdsByNameAsync(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return new List<int>();
        }

        return await Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                return processes.Select(p => p.Id).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get process IDs for: {ProcessName}", processName);
                return new List<int>();
            }
        });
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
                try
                {
                    var ownerInfo = new string[2];
                    var result = process.InvokeMethod("GetOwner", ownerInfo);

                    if (result != null && (uint)result == 0) // Success
                    {
                        var domain = ownerInfo[1];
                        var username = ownerInfo[0];

                        if (!string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(username))
                        {
                            var account = new NTAccount(domain, username);
                            var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
                            return sid.ToString();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get owner for process {ProcessId}", processId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query process owner for process {ProcessId}", processId);
        }

        return null;
    }

    #endregion
}