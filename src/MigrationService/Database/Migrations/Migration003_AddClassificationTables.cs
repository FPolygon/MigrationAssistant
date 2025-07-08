namespace MigrationTool.Service.Database.Migrations;

public class Migration003_AddClassificationTables : IMigration
{
    public int Version => 3;
    public string Description => "Add classification tables for user profile classification and overrides";

    public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            -- User classification results table
            CREATE TABLE IF NOT EXISTS UserClassifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL UNIQUE,
                Classification TEXT NOT NULL,
                ClassificationDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                Confidence REAL NOT NULL DEFAULT 0,
                Reason TEXT NOT NULL,
                RuleSetName TEXT,
                RuleSetVersion TEXT,
                IsOverridden BOOLEAN NOT NULL DEFAULT 0,
                ActivityScore INTEGER,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Classification history table
            CREATE TABLE IF NOT EXISTS ClassificationHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                OldClassification TEXT,
                NewClassification TEXT NOT NULL,
                ChangeDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                Reason TEXT NOT NULL,
                ActivitySnapshot TEXT, -- JSON blob
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Manual classification overrides table
            CREATE TABLE IF NOT EXISTS ClassificationOverrides (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL UNIQUE,
                OverrideClassification TEXT NOT NULL,
                OverrideDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                OverrideBy TEXT NOT NULL,
                Reason TEXT NOT NULL,
                ExpiryDate DATETIME,
                IsActive BOOLEAN NOT NULL DEFAULT 1,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Classification override history table
            CREATE TABLE IF NOT EXISTS ClassificationOverrideHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                OldClassification TEXT,
                NewClassification TEXT,
                ChangeDate DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ChangedBy TEXT NOT NULL,
                Reason TEXT NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Indexes for performance
            CREATE INDEX IF NOT EXISTS idx_classifications_userid ON UserClassifications(UserId);
            CREATE INDEX IF NOT EXISTS idx_classifications_date ON UserClassifications(ClassificationDate);
            CREATE INDEX IF NOT EXISTS idx_classification_history_userid ON ClassificationHistory(UserId);
            CREATE INDEX IF NOT EXISTS idx_classification_history_date ON ClassificationHistory(ChangeDate);
            CREATE INDEX IF NOT EXISTS idx_overrides_userid ON ClassificationOverrides(UserId);
            CREATE INDEX IF NOT EXISTS idx_overrides_active ON ClassificationOverrides(IsActive);
            CREATE INDEX IF NOT EXISTS idx_override_history_userid ON ClassificationOverrideHistory(UserId);

            -- Triggers to update timestamps
            CREATE TRIGGER IF NOT EXISTS update_classification_timestamp 
            AFTER UPDATE ON UserClassifications
            BEGIN
                UPDATE UserClassifications SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;

            CREATE TRIGGER IF NOT EXISTS update_override_timestamp 
            AFTER UPDATE ON ClassificationOverrides
            BEGIN
                UPDATE ClassificationOverrides SET UpdatedAt = CURRENT_TIMESTAMP WHERE Id = NEW.Id;
            END;", cancellationToken);
    }

    public async Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            DROP TRIGGER IF EXISTS update_override_timestamp;
            DROP TRIGGER IF EXISTS update_classification_timestamp;
            DROP INDEX IF EXISTS idx_override_history_userid;
            DROP INDEX IF EXISTS idx_overrides_active;
            DROP INDEX IF EXISTS idx_overrides_userid;
            DROP INDEX IF EXISTS idx_classification_history_date;
            DROP INDEX IF EXISTS idx_classification_history_userid;
            DROP INDEX IF EXISTS idx_classifications_date;
            DROP INDEX IF EXISTS idx_classifications_userid;
            DROP TABLE IF EXISTS ClassificationOverrideHistory;
            DROP TABLE IF EXISTS ClassificationOverrides;
            DROP TABLE IF EXISTS ClassificationHistory;
            DROP TABLE IF EXISTS UserClassifications;", cancellationToken);
    }
}