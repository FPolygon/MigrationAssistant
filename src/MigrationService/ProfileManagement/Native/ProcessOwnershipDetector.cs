using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.ProfileManagement.Native;

/// <summary>
/// Detects process ownership using Windows APIs and WMI
/// </summary>
[SupportedOSPlatform("windows")]
public class ProcessOwnershipDetector : IDisposable
{
    private readonly ILogger<ProcessOwnershipDetector> _logger;
    private readonly ManagementScope? _wmiScope;
    private readonly Dictionary<string, ProcessOwnershipInfo> _processCache;
    private readonly object _cacheLock = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromSeconds(30);

    // Process names that indicate user activity
    private static readonly HashSet<string> UserActivityProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "chrome", "firefox", "edge", "msedge", "iexplore",
        "outlook", "winword", "excel", "powerpnt", "onenote", "teams",
        "slack", "zoom", "notepad", "notepad++", "vscode", "devenv"
    };

    // System processes to exclude
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "smss", "csrss", "wininit", "winlogon", "services",
        "lsass", "svchost", "spoolsv", "searchindexer", "audiodg"
    };

    public ProcessOwnershipDetector(ILogger<ProcessOwnershipDetector> logger)
    {
        _logger = logger;
        _processCache = new Dictionary<string, ProcessOwnershipInfo>();
        
        // Initialize WMI connection
        try
        {
            _wmiScope = new ManagementScope(@"\\.\root\cimv2");
            _wmiScope.Connect();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to WMI");
        }
    }

    /// <summary>
    /// Gets all processes owned by a specific user
    /// </summary>
    public async Task<UserProcessInfo> GetUserProcessesAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting processes for user SID: {Sid}", userSid);

        var userProcessInfo = new UserProcessInfo
        {
            UserSid = userSid,
            ScanTime = DateTime.UtcNow
        };

        try
        {
            // Try WMI first (most accurate)
            await GetProcessesViaWmiAsync(userSid, userProcessInfo, cancellationToken);

            // If WMI failed or returned no results, try Win32 API
            if (!userProcessInfo.Processes.Any() && !userProcessInfo.WmiSucceeded)
            {
                await GetProcessesViaWin32Async(userSid, userProcessInfo, cancellationToken);
            }

            // Analyze process types
            AnalyzeProcessTypes(userProcessInfo);

            _logger.LogInformation(
                "Found {Count} processes for user {Sid}: Interactive={Interactive}, Background={Background}",
                userProcessInfo.TotalProcessCount, userSid, 
                userProcessInfo.InteractiveProcessCount, userProcessInfo.BackgroundProcessCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get processes for user {Sid}", userSid);
            userProcessInfo.Errors.Add($"Process detection failed: {ex.Message}");
        }

        return userProcessInfo;
    }

    /// <summary>
    /// Gets processes using WMI
    /// </summary>
    private async Task GetProcessesViaWmiAsync(string userSid, UserProcessInfo userProcessInfo, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                if (_wmiScope == null || !_wmiScope.IsConnected)
                {
                    _logger.LogWarning("WMI scope not connected");
                    return;
                }

                // WMI query to get all processes
                var query = new ObjectQuery("SELECT ProcessId, Name, ExecutablePath, CreationDate, WorkingSetSize, HandleCount FROM Win32_Process");
                using var searcher = new ManagementObjectSearcher(_wmiScope, query);
                using var results = searcher.Get();

                foreach (ManagementObject process in results)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        var processId = Convert.ToInt32(process["ProcessId"]);
                        
                        // Get process owner
                        var ownerSid = GetProcessOwnerSid(process);
                        if (!string.Equals(ownerSid, userSid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var processName = process["Name"]?.ToString() ?? string.Empty;
                        var executablePath = process["ExecutablePath"]?.ToString() ?? string.Empty;

                        var processInfo = new ProcessInfo
                        {
                            ProcessId = processId,
                            ProcessName = Path.GetFileNameWithoutExtension(processName),
                            ExecutablePath = executablePath,
                            OwnerSid = ownerSid,
                            IsSystemProcess = IsSystemProcess(processName)
                        };

                        // Get additional info
                        if (process["CreationDate"] != null)
                        {
                            var creationDate = ManagementDateTimeConverter.ToDateTime(process["CreationDate"].ToString());
                            processInfo.StartTime = creationDate.ToUniversalTime();
                        }

                        if (process["WorkingSetSize"] != null)
                        {
                            processInfo.WorkingSetSizeBytes = Convert.ToInt64(process["WorkingSetSize"]);
                        }

                        if (process["HandleCount"] != null)
                        {
                            processInfo.HandleCount = Convert.ToInt32(process["HandleCount"]);
                        }

                        // Determine process type
                        DetermineProcessType(processInfo);

                        userProcessInfo.Processes.Add(processInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Error processing WMI process entry");
                    }
                }

                userProcessInfo.WmiSucceeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WMI process enumeration failed");
                userProcessInfo.WmiSucceeded = false;
                userProcessInfo.Errors.Add($"WMI query failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Gets process owner SID from WMI object
    /// </summary>
    private string GetProcessOwnerSid(ManagementObject process)
    {
        try
        {
            // Check cache first
            var processId = Convert.ToInt32(process["ProcessId"]);
            var cacheKey = $"pid_{processId}";

            lock (_cacheLock)
            {
                if (_processCache.TryGetValue(cacheKey, out var cached) &&
                    DateTime.UtcNow - _lastCacheUpdate < _cacheExpiry)
                {
                    return cached.OwnerSid;
                }
            }

            // Call GetOwnerSid method
            var ownerInfo = new object[2];
            var result = process.InvokeMethod("GetOwnerSid", ownerInfo);
            
            if (result != null && ownerInfo[0] is string sid)
            {
                // Cache the result
                lock (_cacheLock)
                {
                    _processCache[cacheKey] = new ProcessOwnershipInfo
                    {
                        ProcessId = processId,
                        OwnerSid = sid
                    };
                    _lastCacheUpdate = DateTime.UtcNow;
                }

                return sid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to get process owner SID");
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets processes using Win32 API (fallback method)
    /// </summary>
    private async Task GetProcessesViaWin32Async(string userSid, UserProcessInfo userProcessInfo, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                var processes = Process.GetProcesses();

                foreach (var process in processes)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        // Skip if we can't get process info
                        if (process.HasExited)
                            continue;

                        var processInfo = new ProcessInfo
                        {
                            ProcessId = process.Id,
                            ProcessName = process.ProcessName,
                            StartTime = GetProcessStartTime(process),
                            WorkingSetSizeBytes = process.WorkingSet64,
                            HandleCount = process.HandleCount,
                            IsSystemProcess = IsSystemProcess(process.ProcessName)
                        };

                        // Try to get executable path
                        try
                        {
                            processInfo.ExecutablePath = process.MainModule?.FileName ?? string.Empty;
                        }
                        catch
                        {
                            // Access denied for some processes
                        }

                        // Try to determine owner (simplified approach)
                        if (IsLikelyUserProcess(process, userSid))
                        {
                            processInfo.OwnerSid = userSid;
                            DetermineProcessType(processInfo);
                            userProcessInfo.Processes.Add(processInfo);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Error processing process {ProcessId}", process.Id);
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                userProcessInfo.Win32Succeeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Win32 process enumeration failed");
                userProcessInfo.Win32Succeeded = false;
                userProcessInfo.Errors.Add($"Win32 enumeration failed: {ex.Message}");
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Gets process start time safely
    /// </summary>
    private DateTime GetProcessStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    /// <summary>
    /// Determines if a process is likely owned by the user (heuristic)
    /// </summary>
    private bool IsLikelyUserProcess(Process process, string userSid)
    {
        // This is a simplified heuristic when we can't get the actual owner
        // In production, you would use OpenProcessToken and GetTokenInformation

        // Check if it's a known user process
        if (UserActivityProcesses.Contains(process.ProcessName))
            return true;

        // Check if it's definitely a system process
        if (SystemProcesses.Contains(process.ProcessName))
            return false;

        // Check session ID (0 is usually services, >0 is usually users)
        try
        {
            if (process.SessionId > 0)
                return true;
        }
        catch
        {
            // Access denied
        }

        return false;
    }

    /// <summary>
    /// Determines if a process is a system process
    /// </summary>
    private bool IsSystemProcess(string processName)
    {
        var name = Path.GetFileNameWithoutExtension(processName);
        return SystemProcesses.Contains(name) || 
               name.StartsWith("svchost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines the type of process
    /// </summary>
    private void DetermineProcessType(ProcessInfo processInfo)
    {
        var processName = processInfo.ProcessName.ToLowerInvariant();

        // Check for UI/Interactive processes
        if (processName == "explorer" || processName == "dwm")
        {
            processInfo.ProcessType = ProcessType.Shell;
            processInfo.IsInteractive = true;
            return;
        }

        // Check for browsers
        if (processName.Contains("chrome") || processName.Contains("firefox") || 
            processName.Contains("edge") || processName == "iexplore")
        {
            processInfo.ProcessType = ProcessType.Browser;
            processInfo.IsInteractive = true;
            return;
        }

        // Check for office apps
        if (processName == "winword" || processName == "excel" || 
            processName == "powerpnt" || processName == "outlook")
        {
            processInfo.ProcessType = ProcessType.Productivity;
            processInfo.IsInteractive = true;
            return;
        }

        // Check for communication apps
        if (processName == "teams" || processName == "slack" || 
            processName == "zoom" || processName.Contains("skype"))
        {
            processInfo.ProcessType = ProcessType.Communication;
            processInfo.IsInteractive = true;
            return;
        }

        // Check for development tools
        if (processName == "devenv" || processName == "code" || 
            processName.Contains("studio") || processName.Contains("idea"))
        {
            processInfo.ProcessType = ProcessType.Development;
            processInfo.IsInteractive = true;
            return;
        }

        // Default to background
        processInfo.ProcessType = ProcessType.Background;
        processInfo.IsInteractive = false;
    }

    /// <summary>
    /// Analyzes process types and counts
    /// </summary>
    private void AnalyzeProcessTypes(UserProcessInfo userProcessInfo)
    {
        userProcessInfo.TotalProcessCount = userProcessInfo.Processes.Count;
        userProcessInfo.InteractiveProcessCount = userProcessInfo.Processes.Count(p => p.IsInteractive);
        userProcessInfo.BackgroundProcessCount = userProcessInfo.Processes.Count(p => !p.IsInteractive);

        // Group by process type
        userProcessInfo.ProcessesByType = userProcessInfo.Processes
            .GroupBy(p => p.ProcessType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Find key processes
        userProcessInfo.HasExplorerProcess = userProcessInfo.Processes.Any(p => p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase));
        userProcessInfo.HasBrowserProcess = userProcessInfo.Processes.Any(p => p.ProcessType == ProcessType.Browser);
        userProcessInfo.HasProductivityProcess = userProcessInfo.Processes.Any(p => p.ProcessType == ProcessType.Productivity);

        // Calculate resource usage
        userProcessInfo.TotalMemoryUsageBytes = userProcessInfo.Processes.Sum(p => p.WorkingSetSizeBytes);
        userProcessInfo.TotalHandleCount = userProcessInfo.Processes.Sum(p => p.HandleCount);
    }

    /// <summary>
    /// Checks if a specific process is running for a user
    /// </summary>
    public async Task<bool> IsProcessRunningForUserAsync(string userSid, string processName, CancellationToken cancellationToken = default)
    {
        var userProcesses = await GetUserProcessesAsync(userSid, cancellationToken);
        return userProcesses.Processes.Any(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets active window information for a user
    /// </summary>
    public async Task<ActiveWindowInfo?> GetActiveWindowForUserAsync(string userSid, CancellationToken cancellationToken = default)
    {
        // This would use GetForegroundWindow and GetWindowThreadProcessId in production
        // For now, return null as it requires P/Invoke
        return await Task.FromResult<ActiveWindowInfo?>(null);
    }

    public void Dispose()
    {
        // ManagementScope doesn't implement IDisposable
        // It's managed by the garbage collector
    }
}

/// <summary>
/// Contains information about processes owned by a user
/// </summary>
public class UserProcessInfo
{
    public string UserSid { get; set; } = string.Empty;
    public DateTime ScanTime { get; set; }
    public List<ProcessInfo> Processes { get; set; } = new();
    
    // Summary statistics
    public int TotalProcessCount { get; set; }
    public int InteractiveProcessCount { get; set; }
    public int BackgroundProcessCount { get; set; }
    public Dictionary<ProcessType, int> ProcessesByType { get; set; } = new();
    
    // Key indicators
    public bool HasExplorerProcess { get; set; }
    public bool HasBrowserProcess { get; set; }
    public bool HasProductivityProcess { get; set; }
    
    // Resource usage
    public long TotalMemoryUsageBytes { get; set; }
    public int TotalHandleCount { get; set; }
    
    // Detection status
    public bool WmiSucceeded { get; set; }
    public bool Win32Succeeded { get; set; }
    public List<string> Errors { get; set; } = new();
}

/// <summary>
/// Information about a single process
/// </summary>
public class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string OwnerSid { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public long WorkingSetSizeBytes { get; set; }
    public int HandleCount { get; set; }
    public ProcessType ProcessType { get; set; }
    public bool IsInteractive { get; set; }
    public bool IsSystemProcess { get; set; }
}

/// <summary>
/// Types of processes
/// </summary>
public enum ProcessType
{
    Unknown,
    Shell,
    Browser,
    Productivity,
    Communication,
    Development,
    Media,
    Game,
    Background,
    Service
}

/// <summary>
/// Information about the active window
/// </summary>
public class ActiveWindowInfo
{
    public string WindowTitle { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public bool IsFullscreen { get; set; }
}

/// <summary>
/// Cached process ownership information
/// </summary>
internal class ProcessOwnershipInfo
{
    public int ProcessId { get; set; }
    public string OwnerSid { get; set; } = string.Empty;
    public DateTime CacheTime { get; set; } = DateTime.UtcNow;
}