namespace MigrationTool.Service.Database.Migrations;

public class Migration001_InitialSchema : IMigration
{
    public int Version => 1;
    public string Description => "Initial database schema with enhanced tables for full migration workflow";

    public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        // UserProfiles table - enhanced with additional fields
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS UserProfiles (
                UserId TEXT PRIMARY KEY,
                UserName TEXT NOT NULL,
                DomainName TEXT,
                ProfilePath TEXT NOT NULL,
                ProfileType TEXT NOT NULL DEFAULT 'Local',
                LastLoginTime DATETIME NOT NULL,
                IsActive BOOLEAN NOT NULL,
                ProfileSizeBytes INTEGER NOT NULL,
                RequiresBackup BOOLEAN NOT NULL DEFAULT 1,
                BackupPriority INTEGER NOT NULL DEFAULT 1,
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
            )", cancellationToken);

        // MigrationStates table - enhanced with proper state tracking
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS MigrationStates (
                UserId TEXT PRIMARY KEY,
                State TEXT NOT NULL DEFAULT 'NotStarted',
                Status TEXT NOT NULL DEFAULT 'Active',
                Progress INTEGER NOT NULL DEFAULT 0,
                StartedAt DATETIME,
                LastUpdated DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CompletedAt DATETIME,
                Deadline DATETIME,
                AttentionReason TEXT,
                DelayCount INTEGER NOT NULL DEFAULT 0,
                IsBlocking BOOLEAN NOT NULL DEFAULT 1,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // BackupOperations table - enhanced for detailed tracking
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS BackupOperations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                OperationId TEXT NOT NULL UNIQUE,
                ProviderName TEXT NOT NULL,
                Category TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAt DATETIME NOT NULL,
                CompletedAt DATETIME,
                Progress INTEGER NOT NULL DEFAULT 0,
                BytesTotal INTEGER NOT NULL DEFAULT 0,
                BytesTransferred INTEGER NOT NULL DEFAULT 0,
                ItemsTotal INTEGER NOT NULL DEFAULT 0,
                ItemsCompleted INTEGER NOT NULL DEFAULT 0,
                ErrorCode TEXT,
                ErrorMessage TEXT,
                RetryCount INTEGER NOT NULL DEFAULT 0,
                ManifestPath TEXT,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // OneDriveSync table - track OneDrive status per user
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS OneDriveSync (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                IsInstalled BOOLEAN NOT NULL,
                IsSignedIn BOOLEAN NOT NULL,
                AccountEmail TEXT,
                SyncFolderPath TEXT,
                SyncStatus TEXT NOT NULL DEFAULT 'Unknown',
                QuotaTotalMB INTEGER,
                QuotaUsedMB INTEGER,
                QuotaAvailableMB INTEGER,
                LastSyncTime DATETIME,
                LastSyncError TEXT,
                ErrorCount INTEGER NOT NULL DEFAULT 0,
                CheckedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // ITEscalations table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ITEscalations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT,
                TriggerType TEXT NOT NULL,
                TriggerReason TEXT NOT NULL,
                Details TEXT,
                TicketNumber TEXT,
                Status TEXT NOT NULL DEFAULT 'Open',
                CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                ResolvedAt DATETIME,
                ResolutionNotes TEXT,
                AutoTriggered BOOLEAN NOT NULL DEFAULT 1,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // StateHistory table - audit trail
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS StateHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                OldState TEXT,
                NewState TEXT NOT NULL,
                Reason TEXT,
                ChangedBy TEXT NOT NULL DEFAULT 'SYSTEM',
                ChangedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                Details TEXT,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // DelayRequests table
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DelayRequests (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                RequestedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                RequestedDelayHours INTEGER NOT NULL,
                Reason TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'Pending',
                ApprovedAt DATETIME,
                NewDeadline DATETIME,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            )", cancellationToken);

        // ProviderResults table - detailed results per backup provider
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS ProviderResults (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                BackupOperationId INTEGER NOT NULL,
                Category TEXT NOT NULL,
                Success BOOLEAN NOT NULL,
                ItemCount INTEGER NOT NULL DEFAULT 0,
                SizeMB INTEGER NOT NULL DEFAULT 0,
                Duration INTEGER NOT NULL DEFAULT 0,
                Details TEXT,
                Errors TEXT,
                FOREIGN KEY (BackupOperationId) REFERENCES BackupOperations(Id)
            )", cancellationToken);

        // SystemEvents table - already exists but adding for completeness
        await connection.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS SystemEvents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                EventType TEXT NOT NULL,
                Severity TEXT NOT NULL,
                Source TEXT NOT NULL DEFAULT 'Service',
                Message TEXT NOT NULL,
                Details TEXT,
                UserId TEXT,
                Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP
            )", cancellationToken);

        // Create indexes for performance
        await connection.ExecuteAsync(@"
            CREATE INDEX IF NOT EXISTS idx_migration_states_status ON MigrationStates(Status);
            CREATE INDEX IF NOT EXISTS idx_migration_states_state ON MigrationStates(State);
            CREATE INDEX IF NOT EXISTS idx_backup_operations_user ON BackupOperations(UserId);
            CREATE INDEX IF NOT EXISTS idx_backup_operations_status ON BackupOperations(Status);
            CREATE INDEX IF NOT EXISTS idx_onedrive_sync_user ON OneDriveSync(UserId);
            CREATE INDEX IF NOT EXISTS idx_escalations_status ON ITEscalations(Status);
            CREATE INDEX IF NOT EXISTS idx_escalations_user ON ITEscalations(UserId);
            CREATE INDEX IF NOT EXISTS idx_state_history_user ON StateHistory(UserId);
            CREATE INDEX IF NOT EXISTS idx_state_history_timestamp ON StateHistory(ChangedAt);
            CREATE INDEX IF NOT EXISTS idx_system_events_timestamp ON SystemEvents(Timestamp);
            CREATE INDEX IF NOT EXISTS idx_delay_requests_user ON DelayRequests(UserId);
            CREATE INDEX IF NOT EXISTS idx_delay_requests_status ON DelayRequests(Status);
        ", cancellationToken);
    }

    public async Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        // Drop all tables in reverse order
        await connection.ExecuteAsync(@"
            DROP TABLE IF EXISTS ProviderResults;
            DROP TABLE IF EXISTS DelayRequests;
            DROP TABLE IF EXISTS StateHistory;
            DROP TABLE IF EXISTS ITEscalations;
            DROP TABLE IF EXISTS OneDriveSync;
            DROP TABLE IF EXISTS BackupOperations;
            DROP TABLE IF EXISTS MigrationStates;
            DROP TABLE IF EXISTS SystemEvents;
            DROP TABLE IF EXISTS UserProfiles;
        ", cancellationToken);
    }
}