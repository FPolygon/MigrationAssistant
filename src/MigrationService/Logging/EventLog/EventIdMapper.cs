using System.Collections.Generic;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.EventLog;

/// <summary>
/// Maps log levels and categories to Windows Event Log event IDs.
/// </summary>
public static class EventIdMapper
{
    // Event ID ranges:
    // 1000-1999: Information events
    // 2000-2999: Warning events  
    // 3000-3999: Error events
    // 4000-4999: Audit events
    // 5000-5999: Performance events

    private static readonly Dictionary<LogLevel, int> _baseLevelIds = new()
    {
        { LogLevel.Information, 1000 },
        { LogLevel.Warning, 2000 },
        { LogLevel.Error, 3000 },
        { LogLevel.Critical, 3500 }
    };

    private static readonly Dictionary<string, int> _categoryOffsets = new()
    {
        // Service lifecycle
        { "MigrationTool.Service", 0 },
        { "MigrationTool.Service.Startup", 1 },
        { "MigrationTool.Service.Shutdown", 2 },
        { "MigrationTool.Service.Recovery", 3 },
        
        // IPC communication
        { "MigrationTool.Service.IPC", 10 },
        { "MigrationTool.Service.IPC.Server", 11 },
        { "MigrationTool.Service.IPC.Client", 12 },
        { "MigrationTool.Service.IPC.Messages", 13 },
        
        // State management
        { "MigrationTool.Service.State", 20 },
        { "MigrationTool.Service.State.Database", 21 },
        { "MigrationTool.Service.State.Migration", 22 },
        
        // User management
        { "MigrationTool.Service.Users", 30 },
        { "MigrationTool.Service.Users.Detection", 31 },
        { "MigrationTool.Service.Users.Classification", 32 },
        
        // Backup operations
        { "MigrationTool.Backup", 40 },
        { "MigrationTool.Backup.Files", 41 },
        { "MigrationTool.Backup.Browsers", 42 },
        { "MigrationTool.Backup.Email", 43 },
        { "MigrationTool.Backup.System", 44 },
        
        // OneDrive integration
        { "MigrationTool.OneDrive", 50 },
        { "MigrationTool.OneDrive.Sync", 51 },
        { "MigrationTool.OneDrive.Quota", 52 },
        
        // Agent operations
        { "MigrationTool.Agent", 60 },
        { "MigrationTool.Agent.Notifications", 61 },
        { "MigrationTool.Agent.UI", 62 },
        
        // Security and audit
        { "MigrationTool.Security", 70 },
        { "MigrationTool.Audit", 80 },
        
        // Performance
        { "MigrationTool.Performance", 90 }
    };

    /// <summary>
    /// Gets the Windows Event ID for a log entry.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <param name="category">The log category.</param>
    /// <returns>The event ID for the Windows Event Log.</returns>
    public static int GetEventId(LogLevel level, string category)
    {
        // Get base ID for the log level
        if (!_baseLevelIds.TryGetValue(level, out var baseId))
        {
            // Default to information level for unmapped levels
            baseId = _baseLevelIds[LogLevel.Information];
        }

        // Get category offset
        var categoryOffset = GetCategoryOffset(category);

        return baseId + categoryOffset;
    }

    /// <summary>
    /// Gets the Windows Event Log event type for a log level.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <returns>The Windows Event Log event type.</returns>
    public static System.Diagnostics.EventLogEntryType GetEventType(LogLevel level)
    {
        return level switch
        {
            LogLevel.Critical => System.Diagnostics.EventLogEntryType.Error,
            LogLevel.Error => System.Diagnostics.EventLogEntryType.Error,
            LogLevel.Warning => System.Diagnostics.EventLogEntryType.Warning,
            LogLevel.Information => System.Diagnostics.EventLogEntryType.Information,
            LogLevel.Debug => System.Diagnostics.EventLogEntryType.Information,
            LogLevel.Verbose => System.Diagnostics.EventLogEntryType.Information,
            _ => System.Diagnostics.EventLogEntryType.Information
        };
    }

    /// <summary>
    /// Gets special event IDs for common scenarios.
    /// </summary>
    public static class SpecialEventIds
    {
        // Service lifecycle events
        public const int ServiceStarted = 1001;
        public const int ServiceStopped = 1002;
        public const int ServicePaused = 1003;
        public const int ServiceResumed = 1004;
        public const int ServiceInstalled = 1005;
        public const int ServiceUninstalled = 1006;

        // Configuration events
        public const int ConfigurationLoaded = 1010;
        public const int ConfigurationChanged = 1011;
        public const int ConfigurationError = 3010;

        // Database events
        public const int DatabaseInitialized = 1020;
        public const int DatabaseMigrated = 1021;
        public const int DatabaseError = 3020;

        // IPC events
        public const int IpcServerStarted = 1030;
        public const int IpcServerStopped = 1031;
        public const int IpcClientConnected = 1032;
        public const int IpcClientDisconnected = 1033;
        public const int IpcError = 3030;

        // Migration events
        public const int MigrationStarted = 1040;
        public const int MigrationCompleted = 1041;
        public const int MigrationFailed = 3040;
        public const int BackupStarted = 1042;
        public const int BackupCompleted = 1043;
        public const int BackupFailed = 3041;

        // Security events (audit range)
        public const int UnauthorizedAccess = 4001;
        public const int SecurityError = 4002;
        public const int PermissionDenied = 4003;

        // Performance events
        public const int PerformanceWarning = 5001;
        public const int PerformanceCritical = 5002;
    }

    private static int GetCategoryOffset(string category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return 0;
        }

        // Try exact match first
        if (_categoryOffsets.TryGetValue(category, out var exactOffset))
        {
            return exactOffset;
        }

        // Try partial matches (find the longest matching prefix)
        var bestMatch = string.Empty;
        var bestOffset = 0;

        foreach (var (prefix, offset) in _categoryOffsets)
        {
            if (category.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase) &&
                prefix.Length > bestMatch.Length)
            {
                bestMatch = prefix;
                bestOffset = offset;
            }
        }

        return bestOffset;
    }
}
