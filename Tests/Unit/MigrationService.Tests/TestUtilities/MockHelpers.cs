using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement.Native;

namespace MigrationService.Tests.TestUtilities;

/// <summary>
/// Mock helper for testing UserActivityData
/// </summary>
public static class UserActivityDataMockHelper
{
    public static UserActivityData CreateMockActivityData(string userSid)
    {
        return new UserActivityData
        {
            UserSid = userSid,
            LastUpdate = DateTime.UtcNow,
            LastInteractiveLogon = DateTime.UtcNow.AddDays(-5),
            LastNetworkLogon = DateTime.UtcNow.AddDays(-2),
            LastLogoff = DateTime.UtcNow.AddDays(-1),
            LastUnlock = DateTime.UtcNow.AddHours(-12),
            LastProfileLoad = DateTime.UtcNow.AddDays(-5),
            IsRegistryLoaded = true,
            RecentDocumentCount = 15,
            RecentRunCommands = 3,
            TypedPathCount = 5,
            HasRecentDocumentActivity = true,
            ProfilePath = @"C:\Users\TestUser",
            NtUserLastModified = DateTime.UtcNow.AddDays(-1),
            HasActiveSession = true,
            HasRdpActivity = false,
            FolderActivity = new Dictionary<string, FolderActivityInfo>
            {
                ["Desktop"] = new FolderActivityInfo
                {
                    Path = @"C:\Users\TestUser\Desktop",
                    RecentFileCount = 5,
                    LastModified = DateTime.UtcNow.AddDays(-2)
                },
                ["Documents"] = new FolderActivityInfo
                {
                    Path = @"C:\Users\TestUser\Documents",
                    RecentFileCount = 10,
                    LastModified = DateTime.UtcNow.AddDays(-1)
                }
            },
            LogonEvents = new List<LogonEvent>
            {
                new LogonEvent
                {
                    EventTime = DateTime.UtcNow.AddDays(-5),
                    EventType = LogonEventType.Logon,
                    LogonType = 2 // Interactive
                },
                new LogonEvent
                {
                    EventTime = DateTime.UtcNow.AddDays(-1),
                    EventType = LogonEventType.Unlock
                }
            }
        };
    }
}

/// <summary>
/// Mock helper for testing UserProcessInfo
/// </summary>
public static class UserProcessInfoMockHelper
{
    public static UserProcessInfo CreateMockProcessInfo(string userSid, int processCount = 5)
    {
        var info = new UserProcessInfo
        {
            UserSid = userSid,
            ScanTime = DateTime.UtcNow,
            WmiSucceeded = true,
            Win32Succeeded = false
        };

        // Add mock processes
        for (int i = 0; i < processCount; i++)
        {
            var process = new ProcessInfo
            {
                ProcessId = 1000 + i,
                ProcessName = GetMockProcessName(i),
                ExecutablePath = $@"C:\Program Files\MockApp{i}\app.exe",
                OwnerSid = userSid,
                StartTime = DateTime.UtcNow.AddHours(-i),
                WorkingSetSizeBytes = (i + 1) * 1024 * 1024,
                HandleCount = (i + 1) * 100,
                ProcessType = GetMockProcessType(i),
                IsInteractive = i < 3,
                IsSystemProcess = false
            };

            info.Processes.Add(process);
        }

        // Calculate statistics
        info.TotalProcessCount = info.Processes.Count;
        info.InteractiveProcessCount = info.Processes.Count(p => p.IsInteractive);
        info.BackgroundProcessCount = info.Processes.Count(p => !p.IsInteractive);
        info.HasExplorerProcess = info.Processes.Any(p => p.ProcessName == "explorer");
        info.HasBrowserProcess = info.Processes.Any(p => p.ProcessType == ProcessType.Browser);
        info.HasProductivityProcess = info.Processes.Any(p => p.ProcessType == ProcessType.Productivity);
        info.TotalMemoryUsageBytes = info.Processes.Sum(p => p.WorkingSetSizeBytes);
        info.TotalHandleCount = info.Processes.Sum(p => p.HandleCount);

        info.ProcessesByType = info.Processes
            .GroupBy(p => p.ProcessType)
            .ToDictionary(g => g.Key, g => g.Count());

        return info;
    }

    private static string GetMockProcessName(int index)
    {
        var names = new[] { "explorer", "chrome", "winword", "notepad", "svchost" };
        return names[index % names.Length];
    }

    private static ProcessType GetMockProcessType(int index)
    {
        var types = new[] { ProcessType.Shell, ProcessType.Browser, ProcessType.Productivity,
            ProcessType.Unknown, ProcessType.Background };
        return types[index % types.Length];
    }
}
