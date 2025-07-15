using System.Runtime.Versioning;
using MigrationTool.Service.OneDrive.Native;

namespace MigrationService.Tests.OneDrive.TestUtilities;

/// <summary>
/// Test implementation of IProcessService for unit testing
/// </summary>
[SupportedOSPlatform("windows")]
public class TestProcessService : IProcessService
{
    private readonly Dictionary<string, List<ProcessInfo>> _processes = new();
    private readonly Dictionary<int, string> _processOwners = new();

    /// <summary>
    /// Configures a process to be running in the test environment
    /// </summary>
    public void SetProcessRunning(string processName, int processId, string? ownerSid = null)
    {
        if (!_processes.ContainsKey(processName))
        {
            _processes[processName] = new List<ProcessInfo>();
        }

        var processInfo = new ProcessInfo
        {
            ProcessId = processId,
            ProcessName = processName,
            OwnerSid = ownerSid
        };

        _processes[processName].Add(processInfo);

        if (ownerSid != null)
        {
            _processOwners[processId] = ownerSid;
        }
    }

    /// <summary>
    /// Removes a process from the test environment
    /// </summary>
    public void RemoveProcess(string processName, int processId)
    {
        if (_processes.ContainsKey(processName))
        {
            _processes[processName].RemoveAll(p => p.ProcessId == processId);
            if (_processes[processName].Count == 0)
            {
                _processes.Remove(processName);
            }
        }

        _processOwners.Remove(processId);
    }

    /// <summary>
    /// Clears all configured processes
    /// </summary>
    public void ClearAllProcesses()
    {
        _processes.Clear();
        _processOwners.Clear();
    }

    /// <inheritdoc/>
    public Task<ProcessInfo[]> GetProcessesByNameAsync(string processName)
    {
        if (_processes.TryGetValue(processName, out var processes))
        {
            return Task.FromResult(processes.ToArray());
        }

        return Task.FromResult(Array.Empty<ProcessInfo>());
    }

    /// <inheritdoc/>
    public Task<string?> GetProcessOwnerSidAsync(int processId)
    {
        _processOwners.TryGetValue(processId, out var ownerSid);
        return Task.FromResult(ownerSid);
    }

    /// <inheritdoc/>
    public Task<bool> IsProcessRunningAsync(string processName)
    {
        return Task.FromResult(_processes.ContainsKey(processName) && _processes[processName].Count > 0);
    }

    /// <inheritdoc/>
    public Task<bool> IsProcessRunningForUserAsync(string processName, string userSid)
    {
        if (_processes.TryGetValue(processName, out var processes))
        {
            return Task.FromResult(processes.Any(p => p.OwnerSid == userSid));
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc/>
    public Task<List<int>> GetProcessIdsByNameAsync(string processName)
    {
        if (_processes.TryGetValue(processName, out var processes))
        {
            return Task.FromResult(processes.Select(p => p.ProcessId).ToList());
        }

        return Task.FromResult(new List<int>());
    }
}