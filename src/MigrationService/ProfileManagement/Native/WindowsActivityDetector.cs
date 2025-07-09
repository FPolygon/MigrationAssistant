using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MigrationTool.Service.ProfileManagement.Native;

/// <summary>
/// Provides native Windows API integration for detecting user activity
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsActivityDetector
{
    private readonly ILogger<WindowsActivityDetector> _logger;
    private readonly TimeSpan _eventLogLookbackPeriod;
    private readonly Dictionary<string, UserActivityData> _activityCache;
    private readonly object _cacheLock = new();
    private DateTime _lastCacheUpdate = DateTime.MinValue;

    // Windows Event IDs for logon/logoff events
    private const int EventIdLogonSuccess = 4624;
    private const int EventIdLogoff = 4634;
    private const int EventIdUnlock = 4801;
    private const int EventIdWorkstationLocked = 4800;
    private const int EventIdRdpConnect = 4778;
    private const int EventIdRdpDisconnect = 4779;

    public WindowsActivityDetector(ILogger<WindowsActivityDetector> logger, TimeSpan? eventLogLookbackPeriod = null)
    {
        _logger = logger;
        _eventLogLookbackPeriod = eventLogLookbackPeriod ?? TimeSpan.FromDays(90);
        _activityCache = new Dictionary<string, UserActivityData>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets comprehensive activity data for a user
    /// </summary>
    public async Task<UserActivityData> GetUserActivityAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting activity data for user SID: {Sid}", userSid);

        // Check cache first
        lock (_cacheLock)
        {
            if (_activityCache.TryGetValue(userSid, out var cachedData) &&
                DateTime.UtcNow - _lastCacheUpdate < TimeSpan.FromMinutes(5))
            {
                _logger.LogDebug("Returning cached activity data for {Sid}", userSid);
                return cachedData;
            }
        }

        var activityData = new UserActivityData
        {
            UserSid = userSid,
            LastUpdate = DateTime.UtcNow
        };

        // Run detection methods in parallel
        var tasks = new List<Task>
        {
            Task.Run(() => DetectLogonEventsAsync(userSid, activityData, cancellationToken), cancellationToken),
            Task.Run(() => DetectRegistryActivity(userSid, activityData), cancellationToken),
            Task.Run(() => DetectProfileActivity(userSid, activityData), cancellationToken),
            Task.Run(() => DetectActiveSessionAsync(userSid, activityData, cancellationToken), cancellationToken)
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Activity detection cancelled for user {Sid}, returning partial results", userSid);
            // Return partial results even if cancelled
        }

        // Update cache
        lock (_cacheLock)
        {
            _activityCache[userSid] = activityData;
            _lastCacheUpdate = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "Activity data for {Sid}: LastLogon={LastLogon}, IsActive={IsActive}, SessionCount={Sessions}",
            userSid, activityData.LastInteractiveLogon, activityData.HasActiveSession, activityData.ActiveSessions.Count);

        return activityData;
    }

    /// <summary>
    /// Detects logon events from Windows Security Event Log
    /// </summary>
    private void DetectLogonEventsAsync(string userSid, UserActivityData activityData, CancellationToken cancellationToken)
    {
        try
        {
            var query = $@"
                <QueryList>
                    <Query Id='0' Path='Security'>
                        <Select Path='Security'>
                            *[System[(EventID={EventIdLogonSuccess} or EventID={EventIdLogoff} or 
                              EventID={EventIdUnlock} or EventID={EventIdRdpConnect}) and 
                              TimeCreated[timediff(@SystemTime) &lt;= {_eventLogLookbackPeriod.TotalMilliseconds}]]]
                        </Select>
                    </Query>
                </QueryList>";

            var eventQuery = new EventLogQuery("Security", PathType.LogName, query);
            using var reader = new EventLogReader(eventQuery);

            EventRecord eventRecord;
            while ((eventRecord = reader.ReadEvent()) != null && !cancellationToken.IsCancellationRequested)
            {
                using (eventRecord)
                {
                    try
                    {
                        // Extract SID from event data
                        var eventSid = ExtractSidFromEvent(eventRecord);
                        if (!string.Equals(eventSid, userSid, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var eventTime = eventRecord.TimeCreated?.ToUniversalTime() ?? DateTime.MinValue;

                        switch (eventRecord.Id)
                        {
                            case EventIdLogonSuccess:
                                var logonType = ExtractLogonType(eventRecord);
                                if (logonType == 2 || logonType == 10) // Interactive or RemoteInteractive
                                {
                                    if (eventTime > activityData.LastInteractiveLogon)
                                    {
                                        activityData.LastInteractiveLogon = eventTime;
                                    }
                                }
                                else if (logonType == 3) // Network
                                {
                                    if (eventTime > activityData.LastNetworkLogon)
                                    {
                                        activityData.LastNetworkLogon = eventTime;
                                    }
                                }
                                activityData.LogonEvents.Add(new LogonEvent
                                {
                                    EventTime = eventTime,
                                    EventType = LogonEventType.Logon,
                                    LogonType = logonType
                                });
                                break;

                            case EventIdLogoff:
                                if (eventTime > activityData.LastLogoff)
                                {
                                    activityData.LastLogoff = eventTime;
                                }

                                activityData.LogonEvents.Add(new LogonEvent
                                {
                                    EventTime = eventTime,
                                    EventType = LogonEventType.Logoff
                                });
                                break;

                            case EventIdUnlock:
                                if (eventTime > activityData.LastUnlock)
                                {
                                    activityData.LastUnlock = eventTime;
                                }

                                activityData.LogonEvents.Add(new LogonEvent
                                {
                                    EventTime = eventTime,
                                    EventType = LogonEventType.Unlock
                                });
                                break;

                            case EventIdRdpConnect:
                                activityData.HasRdpActivity = true;
                                activityData.LogonEvents.Add(new LogonEvent
                                {
                                    EventTime = eventTime,
                                    EventType = LogonEventType.RdpConnect
                                });
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Error processing event record");
                    }
                }
            }

            // Sort events by time
            activityData.LogonEvents.Sort((a, b) => b.EventTime.CompareTo(a.EventTime));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query event log for user {Sid}", userSid);
            activityData.Errors.Add($"Event log query failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects activity from user registry hive
    /// </summary>
    private void DetectRegistryActivity(string userSid, UserActivityData activityData)
    {
        try
        {
            // Check if user registry hive is loaded
            using (var userKey = Registry.Users.OpenSubKey(userSid))
            {
                activityData.IsRegistryLoaded = userKey != null;

                if (userKey != null)
                {
                    // Check various registry locations for activity indicators
                    CheckRecentDocuments(userKey, activityData);
                    CheckRunMRU(userKey, activityData);
                    CheckTypedPaths(userKey, activityData);
                }
            }

            // Check ProfileList for additional data
            var profileListPath = @$"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{userSid}";
            using (var profileKey = Registry.LocalMachine.OpenSubKey(profileListPath))
            {
                if (profileKey != null)
                {
                    // Read local and central profile load times
                    if (profileKey.GetValue("LocalProfileLoadTimeHigh") is int highTime &&
                        profileKey.GetValue("LocalProfileLoadTimeLow") is int lowTime)
                    {
                        var fileTime = ((long)highTime << 32) | (uint)lowTime;
                        var loadTime = DateTime.FromFileTimeUtc(fileTime);
                        if (loadTime > activityData.LastProfileLoad)
                        {
                            activityData.LastProfileLoad = loadTime;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect registry activity for user {Sid}", userSid);
            activityData.Errors.Add($"Registry detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks recent documents in registry
    /// </summary>
    private void CheckRecentDocuments(RegistryKey userKey, UserActivityData activityData)
    {
        try
        {
            using var recentDocsKey = userKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs");
            if (recentDocsKey != null)
            {
                var valueNames = recentDocsKey.GetValueNames();
                activityData.RecentDocumentCount = valueNames.Length;

                // Check MRU list for order
                if (recentDocsKey.GetValue("MRUListEx") is byte[] mruList && mruList.Length >= 4)
                {
                    // First DWORD in MRUListEx is the most recent item
                    activityData.HasRecentDocumentActivity = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error checking recent documents");
        }
    }

    /// <summary>
    /// Checks Run dialog MRU
    /// </summary>
    private void CheckRunMRU(RegistryKey userKey, UserActivityData activityData)
    {
        try
        {
            using var runMruKey = userKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU");
            if (runMruKey != null)
            {
                var valueNames = runMruKey.GetValueNames().Where(n => n != "MRUList").ToArray();
                if (valueNames.Any())
                {
                    activityData.RecentRunCommands = valueNames.Length;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error checking Run MRU");
        }
    }

    /// <summary>
    /// Checks typed paths in Explorer
    /// </summary>
    private void CheckTypedPaths(RegistryKey userKey, UserActivityData activityData)
    {
        try
        {
            using var typedPathsKey = userKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\TypedPaths");
            if (typedPathsKey != null)
            {
                var valueNames = typedPathsKey.GetValueNames();
                activityData.TypedPathCount = valueNames.Length;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Error checking typed paths");
        }
    }

    /// <summary>
    /// Detects profile directory activity
    /// </summary>
    private void DetectProfileActivity(string userSid, UserActivityData activityData)
    {
        try
        {
            // Get profile path from registry
            var profileListPath = @$"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList\{userSid}";
            using var profileKey = Registry.LocalMachine.OpenSubKey(profileListPath);
            if (profileKey?.GetValue("ProfileImagePath") is string profilePath)
            {
                activityData.ProfilePath = profilePath;

                if (Directory.Exists(profilePath))
                {
                    // Check NTUSER.DAT modification time
                    var ntuserPath = Path.Combine(profilePath, "NTUSER.DAT");
                    if (File.Exists(ntuserPath))
                    {
                        var ntuserInfo = new FileInfo(ntuserPath);
                        activityData.NtUserLastModified = ntuserInfo.LastWriteTimeUtc;
                    }

                    // Check key user folders
                    CheckUserFolderActivity(profilePath, activityData);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect profile activity for user {Sid}", userSid);
            activityData.Errors.Add($"Profile activity detection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks activity in user folders
    /// </summary>
    private void CheckUserFolderActivity(string profilePath, UserActivityData activityData)
    {
        var foldersToCheck = new[]
        {
            ("Desktop", "Desktop"),
            ("Documents", "Documents"),
            ("Downloads", "Downloads"),
            ("Recent", @"AppData\Roaming\Microsoft\Windows\Recent")
        };

        foreach (var (name, relativePath) in foldersToCheck)
        {
            try
            {
                var folderPath = Path.Combine(profilePath, relativePath);
                if (Directory.Exists(folderPath))
                {
                    var dirInfo = new DirectoryInfo(folderPath);
                    var recentFiles = dirInfo.GetFiles("*", SearchOption.TopDirectoryOnly)
                        .Where(f => f.LastWriteTimeUtc > DateTime.UtcNow.AddDays(-30))
                        .Count();

                    activityData.FolderActivity[name] = new FolderActivityInfo
                    {
                        Path = folderPath,
                        RecentFileCount = recentFiles,
                        LastModified = dirInfo.LastWriteTimeUtc
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Error checking folder activity for {Folder}", name);
            }
        }
    }

    /// <summary>
    /// Detects active sessions using Windows APIs
    /// </summary>
    private async Task DetectActiveSessionAsync(string userSid, UserActivityData activityData, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            try
            {
                // This would use WTSEnumerateSessions API in production
                // For now, simplified implementation
                activityData.HasActiveSession = IsUserLoggedOn(userSid);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to detect active session for user {Sid}", userSid);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Checks if user is currently logged on
    /// </summary>
    private bool IsUserLoggedOn(string userSid)
    {
        try
        {
            // Check if user's registry hive is loaded (simple check)
            using var userKey = Registry.Users.OpenSubKey(userSid);
            return userKey != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Extracts SID from event record
    /// </summary>
    private string ExtractSidFromEvent(EventRecord eventRecord)
    {
        try
        {
            if (eventRecord.UserId != null)
            {
                return eventRecord.UserId.Value;
            }

            // Try to extract from event data
            var eventXml = eventRecord.ToXml();
            var startIndex = eventXml.IndexOf("<Data Name='TargetUserSid'>", StringComparison.OrdinalIgnoreCase);
            if (startIndex > 0)
            {
                startIndex += 27;
                var endIndex = eventXml.IndexOf("</Data>", startIndex, StringComparison.OrdinalIgnoreCase);
                if (endIndex > startIndex)
                {
                    return eventXml.Substring(startIndex, endIndex - startIndex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to extract SID from event");
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts logon type from event record
    /// </summary>
    private int ExtractLogonType(EventRecord eventRecord)
    {
        try
        {
            var eventXml = eventRecord.ToXml();
            var startIndex = eventXml.IndexOf("<Data Name='LogonType'>", StringComparison.OrdinalIgnoreCase);
            if (startIndex > 0)
            {
                startIndex += 23;
                var endIndex = eventXml.IndexOf("</Data>", startIndex, StringComparison.OrdinalIgnoreCase);
                if (endIndex > startIndex)
                {
                    var logonTypeStr = eventXml.Substring(startIndex, endIndex - startIndex);
                    if (int.TryParse(logonTypeStr, out var logonType))
                    {
                        return logonType;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Failed to extract logon type from event");
        }

        return 0;
    }

    /// <summary>
    /// Clears the activity cache
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            _activityCache.Clear();
            _lastCacheUpdate = DateTime.MinValue;
        }
    }
}

/// <summary>
/// Represents user activity data
/// </summary>
public class UserActivityData
{
    public string UserSid { get; set; } = string.Empty;
    public DateTime LastUpdate { get; set; }

    // Logon information
    public DateTime LastInteractiveLogon { get; set; }
    public DateTime LastNetworkLogon { get; set; }
    public DateTime LastLogoff { get; set; }
    public DateTime LastUnlock { get; set; }
    public DateTime LastProfileLoad { get; set; }

    // Registry activity
    public bool IsRegistryLoaded { get; set; }
    public int RecentDocumentCount { get; set; }
    public int RecentRunCommands { get; set; }
    public int TypedPathCount { get; set; }
    public bool HasRecentDocumentActivity { get; set; }

    // Profile activity
    public string ProfilePath { get; set; } = string.Empty;
    public DateTime NtUserLastModified { get; set; }
    public Dictionary<string, FolderActivityInfo> FolderActivity { get; set; } = new();

    // Session information
    public bool HasActiveSession { get; set; }
    public bool HasRdpActivity { get; set; }
    public List<SessionInfo> ActiveSessions { get; set; } = new();

    // Event data
    public List<LogonEvent> LogonEvents { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// Gets the most recent activity time
    /// </summary>
    public DateTime MostRecentActivity
    {
        get
        {
            var times = new[]
            {
                LastInteractiveLogon,
                LastNetworkLogon,
                LastUnlock,
                LastProfileLoad,
                NtUserLastModified
            };

            var folderTimes = FolderActivity.Values.Select(f => f.LastModified);
            return times.Concat(folderTimes).Where(t => t != DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();
        }
    }
}

/// <summary>
/// Represents folder activity information
/// </summary>
public class FolderActivityInfo
{
    public string Path { get; set; } = string.Empty;
    public int RecentFileCount { get; set; }
    public DateTime LastModified { get; set; }
}

/// <summary>
/// Represents a logon event
/// </summary>
public class LogonEvent
{
    public DateTime EventTime { get; set; }
    public LogonEventType EventType { get; set; }
    public int LogonType { get; set; }
}

/// <summary>
/// Types of logon events
/// </summary>
public enum LogonEventType
{
    Logon,
    Logoff,
    Unlock,
    Lock,
    RdpConnect,
    RdpDisconnect
}

/// <summary>
/// Represents session information
/// </summary>
public class SessionInfo
{
    public int SessionId { get; set; }
    public string SessionName { get; set; } = string.Empty;
    public SessionState State { get; set; }
    public DateTime ConnectTime { get; set; }
}

/// <summary>
/// Session states
/// </summary>
public enum SessionState
{
    Active,
    Connected,
    ConnectQuery,
    Shadow,
    Disconnected,
    Idle,
    Listen,
    Reset,
    Down,
    Init
}
