using System.Text.Json.Serialization;

namespace MigrationTool.Service.Models;

/// <summary>
/// Represents the backup space requirements for a user
/// </summary>
public class BackupRequirements
{
    public string UserId { get; set; } = string.Empty;
    public long ProfileSizeMB { get; set; }
    public long EstimatedBackupSizeMB { get; set; }
    public double CompressionFactor { get; set; } = 0.7;
    public long RequiredSpaceMB { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public BackupSizeBreakdown Breakdown { get; set; } = new();

    [JsonIgnore]
    public long EstimatedCompressedSizeMB => (long)(EstimatedBackupSizeMB * CompressionFactor);
}

/// <summary>
/// Detailed breakdown of backup space requirements by category
/// </summary>
public class BackupSizeBreakdown
{
    public long UserFilesMB { get; set; }
    public long AppDataMB { get; set; }
    public long BrowserDataMB { get; set; }
    public long EmailDataMB { get; set; }
    public long SystemConfigMB { get; set; }
    public long TemporaryFilesMB { get; set; }

    [JsonIgnore]
    public long TotalMB => UserFilesMB + AppDataMB + BrowserDataMB + EmailDataMB + SystemConfigMB + TemporaryFilesMB;
}

/// <summary>
/// Current quota health status for a user's OneDrive
/// </summary>
public class QuotaStatus
{
    public string UserId { get; set; } = string.Empty;
    public long TotalSpaceMB { get; set; }
    public long UsedSpaceMB { get; set; }
    public long AvailableSpaceMB { get; set; }
    public long RequiredSpaceMB { get; set; }
    public QuotaHealthLevel HealthLevel { get; set; } = QuotaHealthLevel.Unknown;
    public double UsagePercentage { get; set; }
    public bool CanAccommodateBackup { get; set; }
    public long ShortfallMB { get; set; }
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public List<string> Issues { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

/// <summary>
/// Health levels for quota status
/// </summary>
public enum QuotaHealthLevel
{
    Unknown,
    Healthy,        // < 80% used, can accommodate backup
    Warning,        // 80-95% used, or backup might not fit
    Critical,       // > 95% used, or backup definitely won't fit
    Exceeded        // 100% used or insufficient space for backup
}

/// <summary>
/// Warning levels for quota warnings
/// </summary>
public enum QuotaWarningLevel
{
    Info,
    Warning,
    Critical,
    Emergency
}

/// <summary>
/// Represents a quota warning or alert
/// </summary>
public class QuotaWarning
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public QuotaWarningLevel Level { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public QuotaWarningType Type { get; set; }
    public long CurrentUsageMB { get; set; }
    public long AvailableSpaceMB { get; set; }
    public long RequiredSpaceMB { get; set; }
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolutionNotes { get; set; }
}

/// <summary>
/// Types of quota warnings
/// </summary>
public enum QuotaWarningType
{
    HighUsage,              // Usage above warning threshold
    InsufficientSpace,      // Not enough space for backup
    BackupTooLarge,         // Backup requirements exceed available space
    QuotaExceeded,          // Already at 100% capacity
    PredictedShortfall,     // Will run out of space during backup
    ConfigurationIssue      // OneDrive configuration problem
}

/// <summary>
/// Detailed information about a quota issue for IT escalation
/// </summary>
public class QuotaIssueDetails
{
    public string UserId { get; set; } = string.Empty;
    public string UserDisplayName { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public QuotaIssueType IssueType { get; set; }
    public string IssueDescription { get; set; } = string.Empty;
    public QuotaStatus CurrentStatus { get; set; } = new();
    public BackupRequirements BackupRequirements { get; set; } = new();
    public List<string> TechnicalDetails { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    public QuotaWarningLevel Severity { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public bool RequiresImmediateAction { get; set; }
}

/// <summary>
/// Types of quota issues that may require IT escalation
/// </summary>
public enum QuotaIssueType
{
    InsufficientQuota,      // User needs more OneDrive space
    BackupTooLarge,         // User's data exceeds reasonable backup size
    OneDriveNotConfigured,  // OneDrive not properly set up
    MultipleAccounts,       // Multiple OneDrive accounts causing confusion
    SyncError,              // Sync issues preventing proper quota detection
    ConfigurationError,     // System configuration preventing proper operation
    AccountLimitation       // Business account limitations
}

/// <summary>
/// Database record for storing quota status
/// </summary>
public class QuotaStatusRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long TotalSpaceMB { get; set; }
    public long UsedSpaceMB { get; set; }
    public long AvailableSpaceMB { get; set; }
    public long RequiredSpaceMB { get; set; }
    public string HealthLevel { get; set; } = QuotaHealthLevel.Unknown.ToString();
    public double UsagePercentage { get; set; }
    public bool CanAccommodateBackup { get; set; }
    public long ShortfallMB { get; set; }
    public string? Issues { get; set; } // JSON serialized
    public string? Recommendations { get; set; } // JSON serialized
    public DateTime LastChecked { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Database record for storing backup requirements
/// </summary>
public class BackupRequirementsRecord
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public long ProfileSizeMB { get; set; }
    public long EstimatedBackupSizeMB { get; set; }
    public double CompressionFactor { get; set; } = 0.7;
    public long RequiredSpaceMB { get; set; }
    public string? SizeBreakdown { get; set; } // JSON serialized BackupSizeBreakdown
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Database record for quota escalations to IT
/// </summary>
public class QuotaEscalation
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string IssueDescription { get; set; } = string.Empty;
    public string TechnicalDetails { get; set; } = string.Empty; // JSON serialized
    public string RecommendedActions { get; set; } = string.Empty; // JSON serialized
    public bool RequiresImmediateAction { get; set; }
    public bool IsResolved { get; set; }
    public string? TicketNumber { get; set; }
    public string? ResolutionNotes { get; set; }
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

/// <summary>
/// Configuration options for quota management
/// </summary>
public class QuotaManagementConfiguration
{
    /// <summary>
    /// Percentage threshold for quota warnings (default: 80%)
    /// </summary>
    public double WarningThresholdPercent { get; set; } = 80.0;

    /// <summary>
    /// Percentage threshold for critical quota alerts (default: 95%)
    /// </summary>
    public double CriticalThresholdPercent { get; set; } = 95.0;

    /// <summary>
    /// Expected compression factor for backup data (default: 0.7)
    /// </summary>
    public double BackupCompressionFactor { get; set; } = 0.7;

    /// <summary>
    /// Additional safety margin in MB for backup calculations (default: 1024 MB)
    /// </summary>
    public long SafetyMarginMB { get; set; } = 1024;

    /// <summary>
    /// Interval in minutes for proactive quota checking (default: 30)
    /// </summary>
    public int QuotaCheckIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum time in seconds to spend calculating backup requirements (default: 300)
    /// </summary>
    public int CalculationTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Whether to automatically escalate critical quota issues (default: true)
    /// </summary>
    public bool AutoEscalateCriticalIssues { get; set; } = true;

    /// <summary>
    /// Minimum free space required in MB regardless of backup size (default: 2048 MB)
    /// </summary>
    public long MinimumFreeSpaceMB { get; set; } = 2048;
}

/// <summary>
/// Comprehensive quota metrics for reporting and analysis
/// </summary>
public class QuotaMetrics
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public Dictionary<string, Dictionary<string, int>> WarningCountsByType { get; set; } = new();
    public Dictionary<string, Dictionary<string, int>> EscalationCountsByType { get; set; } = new();
    public double AverageSpaceUsagePercentage { get; set; }
    public double MinSpaceUsagePercentage { get; set; }
    public double MaxSpaceUsagePercentage { get; set; }
    public int TotalUsersAnalyzed { get; set; }
    public int UsersWithSufficientSpace { get; set; }
    public int UsersRequiringAttention { get; set; }
}
