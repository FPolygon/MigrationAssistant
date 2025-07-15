namespace MigrationTool.Service.Database.Migrations;

public class Migration004_AddOneDriveTables : IMigration
{
    public int Version => 4;
    public string Description => "Add OneDrive status and sync folder tracking tables";

    public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(@"
            -- OneDrive status table
            CREATE TABLE IF NOT EXISTS OneDriveStatus (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL UNIQUE,
                IsInstalled BOOLEAN NOT NULL DEFAULT 0,
                IsRunning BOOLEAN NOT NULL DEFAULT 0,
                IsSignedIn BOOLEAN NOT NULL DEFAULT 0,
                AccountEmail TEXT,
                PrimaryAccountId TEXT,
                SyncFolder TEXT,
                SyncStatus TEXT NOT NULL,
                AvailableSpaceMB INTEGER,
                UsedSpaceMB INTEGER,
                HasSyncErrors BOOLEAN NOT NULL DEFAULT 0,
                ErrorDetails TEXT,
                LastChecked DATETIME NOT NULL,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- OneDrive accounts table (supports multiple accounts per user)
            CREATE TABLE IF NOT EXISTS OneDriveAccounts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                AccountId TEXT NOT NULL,
                Email TEXT NOT NULL,
                DisplayName TEXT,
                UserFolder TEXT NOT NULL,
                IsPrimary BOOLEAN NOT NULL DEFAULT 0,
                LastSyncTime DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(UserId, AccountId),
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Synced folders table (includes SharePoint libraries)
            CREATE TABLE IF NOT EXISTS OneDriveSyncedFolders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                AccountId TEXT,
                LocalPath TEXT NOT NULL,
                RemotePath TEXT,
                FolderType TEXT NOT NULL, -- Personal, Business, SharePointLibrary, KnownFolder
                DisplayName TEXT,
                SharePointSiteUrl TEXT,
                LibraryName TEXT,
                SizeBytes INTEGER,
                FileCount INTEGER,
                IsSyncing BOOLEAN NOT NULL DEFAULT 0,
                HasErrors BOOLEAN NOT NULL DEFAULT 0,
                LastSyncCheck DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId),
                FOREIGN KEY (UserId, AccountId) REFERENCES OneDriveAccounts(UserId, AccountId)
            );

            -- Known Folder Move configuration table
            CREATE TABLE IF NOT EXISTS KnownFolderMoveStatus (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                AccountId TEXT NOT NULL,
                IsEnabled BOOLEAN NOT NULL DEFAULT 0,
                DesktopRedirected BOOLEAN NOT NULL DEFAULT 0,
                DesktopPath TEXT,
                DocumentsRedirected BOOLEAN NOT NULL DEFAULT 0,
                DocumentsPath TEXT,
                PicturesRedirected BOOLEAN NOT NULL DEFAULT 0,
                PicturesPath TEXT,
                ConfigurationSource TEXT,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UpdatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                UNIQUE(UserId, AccountId),
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId),
                FOREIGN KEY (UserId, AccountId) REFERENCES OneDriveAccounts(UserId, AccountId)
            );

            -- Sync errors table for detailed error tracking
            CREATE TABLE IF NOT EXISTS OneDriveSyncErrors (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                UserId TEXT NOT NULL,
                FolderPath TEXT NOT NULL,
                FilePath TEXT,
                ErrorMessage TEXT NOT NULL,
                ErrorCode TEXT,
                IsRecoverable BOOLEAN NOT NULL DEFAULT 1,
                AttemptedRecovery BOOLEAN NOT NULL DEFAULT 0,
                RecoveryResult TEXT,
                ErrorTime DATETIME NOT NULL,
                ResolvedTime DATETIME,
                CreatedAt DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                FOREIGN KEY (UserId) REFERENCES UserProfiles(UserId)
            );

            -- Create indexes for performance
            CREATE INDEX IF NOT EXISTS idx_onedrive_status_userid ON OneDriveStatus(UserId);
            CREATE INDEX IF NOT EXISTS idx_onedrive_accounts_userid ON OneDriveAccounts(UserId);
            CREATE INDEX IF NOT EXISTS idx_synced_folders_userid ON OneDriveSyncedFolders(UserId);
            CREATE INDEX IF NOT EXISTS idx_synced_folders_localpath ON OneDriveSyncedFolders(LocalPath);
            CREATE INDEX IF NOT EXISTS idx_sync_errors_userid ON OneDriveSyncErrors(UserId);
            CREATE INDEX IF NOT EXISTS idx_sync_errors_errortime ON OneDriveSyncErrors(ErrorTime);
        ", cancellationToken);
    }

    public async Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
    {
        // Drop indexes first
        await connection.ExecuteAsync(@"
            DROP INDEX IF EXISTS idx_sync_errors_errortime;
            DROP INDEX IF EXISTS idx_sync_errors_userid;
            DROP INDEX IF EXISTS idx_synced_folders_localpath;
            DROP INDEX IF EXISTS idx_synced_folders_userid;
            DROP INDEX IF EXISTS idx_onedrive_accounts_userid;
            DROP INDEX IF EXISTS idx_onedrive_status_userid;
        ", cancellationToken);

        // Drop tables in reverse order due to foreign key constraints
        await connection.ExecuteAsync(@"
            DROP TABLE IF EXISTS OneDriveSyncErrors;
            DROP TABLE IF EXISTS KnownFolderMoveStatus;
            DROP TABLE IF EXISTS OneDriveSyncedFolders;
            DROP TABLE IF EXISTS OneDriveAccounts;
            DROP TABLE IF EXISTS OneDriveStatus;
        ", cancellationToken);
    }
}
