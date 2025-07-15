namespace MigrationTool.Service.Database.Migrations;

public class Migration005_AddSyncTracking : IMigration
{
    public int Version => 5;
    public string Description => "Add sync operation tracking and error management tables";

    public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            -- Sync operations table for tracking sync attempts and progress
            CREATE TABLE IF NOT EXISTS SyncOperations (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserSid TEXT NOT NULL,
                FolderPath TEXT NOT NULL,
                StartTime DATETIME NOT NULL,
                EndTime DATETIME,
                Status TEXT NOT NULL,
                FilesTotal INTEGER,
                FilesUploaded INTEGER,
                BytesTotal INTEGER,
                BytesUploaded INTEGER,
                LocalOnlyFiles INTEGER,
                ErrorCount INTEGER DEFAULT 0,
                RetryCount INTEGER DEFAULT 0,
                LastRetryTime DATETIME,
                EstimatedTimeRemaining INTEGER, -- seconds
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserSid) REFERENCES UserProfiles(UserId)
            );

            -- Sync errors table for detailed error tracking
            CREATE TABLE IF NOT EXISTS SyncErrors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                SyncOperationId INTEGER NOT NULL,
                FilePath TEXT NOT NULL,
                ErrorCode TEXT,
                ErrorMessage TEXT NOT NULL,
                ErrorTime DATETIME NOT NULL,
                RetryAttempts INTEGER DEFAULT 0,
                IsResolved BOOLEAN DEFAULT 0,
                ResolvedAt DATETIME,
                EscalatedToIT BOOLEAN DEFAULT 0,
                EscalatedAt DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (SyncOperationId) REFERENCES SyncOperations(Id)
            );

            -- Index for efficient queries
            CREATE INDEX IF NOT EXISTS idx_sync_operations_user_folder ON SyncOperations(UserSid, FolderPath);
            CREATE INDEX IF NOT EXISTS idx_sync_operations_status ON SyncOperations(Status);
            CREATE INDEX IF NOT EXISTS idx_sync_errors_operation ON SyncErrors(SyncOperationId);
            CREATE INDEX IF NOT EXISTS idx_sync_errors_unresolved ON SyncErrors(IsResolved, EscalatedToIT);

            -- Trigger to update timestamps
            CREATE TRIGGER IF NOT EXISTS update_sync_operations_timestamp
            AFTER UPDATE ON SyncOperations
            BEGIN
                UPDATE SyncOperations SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS update_sync_errors_timestamp
            AFTER UPDATE ON SyncErrors
            BEGIN
                UPDATE SyncErrors SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;
        ", cancellationToken);
    }

    public async Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            DROP TRIGGER IF EXISTS update_sync_errors_timestamp;
            DROP TRIGGER IF EXISTS update_sync_operations_timestamp;
            DROP INDEX IF EXISTS idx_sync_errors_unresolved;
            DROP INDEX IF EXISTS idx_sync_errors_operation;
            DROP INDEX IF EXISTS idx_sync_operations_status;
            DROP INDEX IF EXISTS idx_sync_operations_user_folder;
            DROP TABLE IF EXISTS SyncErrors;
            DROP TABLE IF EXISTS SyncOperations;
        ", cancellationToken);
    }
}
