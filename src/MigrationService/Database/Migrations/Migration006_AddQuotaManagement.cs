namespace MigrationTool.Service.Database.Migrations;

public class Migration006_AddQuotaManagement : IMigration
{
    public int Version => 6;
    public string Description => "Add quota management tables for backup requirements, quota status, and escalations";

    public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            -- Quota status table for tracking current quota health for each user
            CREATE TABLE IF NOT EXISTS QuotaStatus (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                TotalSpaceMB INTEGER NOT NULL,
                UsedSpaceMB INTEGER NOT NULL,
                AvailableSpaceMB INTEGER NOT NULL,
                RequiredSpaceMB INTEGER NOT NULL,
                HealthLevel TEXT NOT NULL,
                UsagePercentage REAL NOT NULL,
                CanAccommodateBackup BOOLEAN NOT NULL,
                ShortfallMB INTEGER NOT NULL,
                Issues TEXT, -- JSON serialized array of strings
                Recommendations TEXT, -- JSON serialized array of strings
                LastChecked DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Backup requirements table for storing calculated space requirements
            CREATE TABLE IF NOT EXISTS BackupRequirements (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                ProfileSizeMB INTEGER NOT NULL,
                EstimatedBackupSizeMB INTEGER NOT NULL,
                CompressionFactor REAL NOT NULL DEFAULT 0.7,
                RequiredSpaceMB INTEGER NOT NULL,
                SizeBreakdown TEXT, -- JSON serialized BackupSizeBreakdown
                CalculatedAt DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Quota warnings table for tracking quota alerts and warnings
            CREATE TABLE IF NOT EXISTS QuotaWarnings (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                Level TEXT NOT NULL, -- Info, Warning, Critical, Emergency
                Title TEXT NOT NULL,
                Message TEXT NOT NULL,
                Type TEXT NOT NULL, -- HighUsage, InsufficientSpace, etc.
                CurrentUsageMB INTEGER NOT NULL,
                AvailableSpaceMB INTEGER NOT NULL,
                RequiredSpaceMB INTEGER NOT NULL,
                IsResolved BOOLEAN NOT NULL DEFAULT 0,
                ResolvedAt DATETIME,
                ResolutionNotes TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Quota escalations table for IT escalation tracking
            CREATE TABLE IF NOT EXISTS QuotaEscalations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                IssueType TEXT NOT NULL,
                Severity TEXT NOT NULL,
                IssueDescription TEXT NOT NULL,
                TechnicalDetails TEXT, -- JSON serialized array of strings
                RecommendedActions TEXT, -- JSON serialized array of strings
                RequiresImmediateAction BOOLEAN NOT NULL DEFAULT 0,
                IsResolved BOOLEAN NOT NULL DEFAULT 0,
                TicketNumber TEXT,
                ResolutionNotes TEXT,
                DetectedAt DATETIME NOT NULL,
                ResolvedAt DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Indexes for efficient queries
            CREATE INDEX IF NOT EXISTS idx_quota_status_user ON QuotaStatus(UserId);
            CREATE INDEX IF NOT EXISTS idx_quota_status_health ON QuotaStatus(HealthLevel);
            CREATE INDEX IF NOT EXISTS idx_quota_status_last_checked ON QuotaStatus(LastChecked);
            
            CREATE INDEX IF NOT EXISTS idx_backup_requirements_user ON BackupRequirements(UserId);
            CREATE INDEX IF NOT EXISTS idx_backup_requirements_calculated ON BackupRequirements(CalculatedAt);
            
            CREATE INDEX IF NOT EXISTS idx_quota_warnings_user ON QuotaWarnings(UserId);
            CREATE INDEX IF NOT EXISTS idx_quota_warnings_level ON QuotaWarnings(Level);
            CREATE INDEX IF NOT EXISTS idx_quota_warnings_unresolved ON QuotaWarnings(IsResolved);
            CREATE INDEX IF NOT EXISTS idx_quota_warnings_type ON QuotaWarnings(Type);
            
            CREATE INDEX IF NOT EXISTS idx_quota_escalations_user ON QuotaEscalations(UserId);
            CREATE INDEX IF NOT EXISTS idx_quota_escalations_severity ON QuotaEscalations(Severity);
            CREATE INDEX IF NOT EXISTS idx_quota_escalations_unresolved ON QuotaEscalations(IsResolved);
            CREATE INDEX IF NOT EXISTS idx_quota_escalations_immediate ON QuotaEscalations(RequiresImmediateAction);

            -- Triggers to update timestamps automatically
            CREATE TRIGGER IF NOT EXISTS update_quota_status_timestamp
            AFTER UPDATE ON QuotaStatus
            BEGIN
                UPDATE QuotaStatus SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS update_backup_requirements_timestamp
            AFTER UPDATE ON BackupRequirements
            BEGIN
                UPDATE BackupRequirements SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS update_quota_warnings_timestamp
            AFTER UPDATE ON QuotaWarnings
            BEGIN
                UPDATE QuotaWarnings SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS update_quota_escalations_timestamp
            AFTER UPDATE ON QuotaEscalations
            BEGIN
                UPDATE QuotaEscalations SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;
        ", cancellationToken);
    }

    public async Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            -- Drop triggers
            DROP TRIGGER IF EXISTS update_quota_escalations_timestamp;
            DROP TRIGGER IF EXISTS update_quota_warnings_timestamp;
            DROP TRIGGER IF EXISTS update_backup_requirements_timestamp;
            DROP TRIGGER IF EXISTS update_quota_status_timestamp;

            -- Drop indexes
            DROP INDEX IF EXISTS idx_quota_escalations_immediate;
            DROP INDEX IF EXISTS idx_quota_escalations_unresolved;
            DROP INDEX IF EXISTS idx_quota_escalations_severity;
            DROP INDEX IF EXISTS idx_quota_escalations_user;
            
            DROP INDEX IF EXISTS idx_quota_warnings_type;
            DROP INDEX IF EXISTS idx_quota_warnings_unresolved;
            DROP INDEX IF EXISTS idx_quota_warnings_level;
            DROP INDEX IF EXISTS idx_quota_warnings_user;
            
            DROP INDEX IF EXISTS idx_backup_requirements_calculated;
            DROP INDEX IF EXISTS idx_backup_requirements_user;
            
            DROP INDEX IF EXISTS idx_quota_status_last_checked;
            DROP INDEX IF EXISTS idx_quota_status_health;
            DROP INDEX IF EXISTS idx_quota_status_user;

            -- Drop tables
            DROP TABLE IF EXISTS QuotaEscalations;
            DROP TABLE IF EXISTS QuotaWarnings;
            DROP TABLE IF EXISTS BackupRequirements;
            DROP TABLE IF EXISTS QuotaStatus;
        ", cancellationToken);
    }
}
