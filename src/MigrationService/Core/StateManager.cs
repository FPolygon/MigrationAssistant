using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service.Database;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;

namespace MigrationTool.Service.Core;

public class StateManager : IStateManager, IDisposable
{
    private readonly ILogger<StateManager> _logger;
    private readonly ServiceConfiguration _configuration;
    private readonly string _connectionString;
    private readonly MigrationRunner _migrationRunner;
    private bool _isInitialized;

    public StateManager(
        ILogger<StateManager> logger,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _configuration = configuration.Value;

        var dbPath = Path.Combine(_configuration.DataPath, "migration.db");
        _connectionString = $"Data Source={dbPath};Mode=ReadWriteCreate;Cache=Shared;";

        var migrationRunnerLogger = new LoggerFactory()
            .CreateLogger<MigrationRunner>();
        _migrationRunner = new MigrationRunner(migrationRunnerLogger, _connectionString);
    }

    #region Initialization and Health

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        _logger.LogInformation("Initializing state database");

        try
        {
            // Ensure database directory exists
            var dbDirectory = Path.GetDirectoryName(Path.Combine(_configuration.DataPath, "migration.db"));
            if (!string.IsNullOrEmpty(dbDirectory) && !Directory.Exists(dbDirectory))
            {
                Directory.CreateDirectory(dbDirectory);
            }

            // Run database migrations
            await _migrationRunner.RunMigrationsAsync(cancellationToken);

            // Enable WAL mode for better concurrency
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            _isInitialized = true;
            _logger.LogInformation("State database initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize state database");
            throw;
        }
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Use a connection string without ReadWriteCreate to avoid creating a new DB
            var healthCheckConnectionString = _connectionString.Replace("Mode=ReadWriteCreate;", "Mode=ReadWrite;");
            using var connection = new SqliteConnection(healthCheckConnectionString);
            await connection.OpenAsync(cancellationToken);

            // Check if essential tables exist
            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM sqlite_master 
                WHERE type='table' AND name IN ('UserProfiles', 'MigrationStates', 'BackupOperations')";

            var tableCount = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

            // We expect at least these 3 core tables to exist
            return tableCount >= 3;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return false;
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush database");
        }
    }

    #endregion

    #region User Profile Management

    public async Task<IEnumerable<UserProfile>> GetUserProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<UserProfile>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId, UserName, DomainName, ProfilePath, ProfileType, 
                       LastLoginTime, IsActive, ProfileSizeBytes, RequiresBackup, 
                       BackupPriority, CreatedAt, UpdatedAt
                FROM UserProfiles
                ORDER BY BackupPriority DESC, LastLoginTime DESC";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                profiles.Add(ReadUserProfile(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user profiles");
        }

        return profiles;
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId, UserName, DomainName, ProfilePath, ProfileType, 
                       LastLoginTime, IsActive, ProfileSizeBytes, RequiresBackup, 
                       BackupPriority, CreatedAt, UpdatedAt
                FROM UserProfiles
                WHERE UserId = @userId";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadUserProfile(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user profile for {UserId}", userId);
        }

        return null;
    }

    public async Task UpdateUserProfileAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO UserProfiles 
                (UserId, UserName, DomainName, ProfilePath, ProfileType, LastLoginTime, 
                 IsActive, ProfileSizeBytes, RequiresBackup, BackupPriority, UpdatedAt) 
                VALUES (@userId, @userName, @domainName, @profilePath, @profileType, 
                        @lastLoginTime, @isActive, @profileSize, @requiresBackup, 
                        @backupPriority, CURRENT_TIMESTAMP)";

            command.Parameters.AddWithValue("@userId", profile.UserId);
            command.Parameters.AddWithValue("@userName", profile.UserName);
            command.Parameters.AddWithValue("@domainName", profile.DomainName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@profilePath", profile.ProfilePath);
            command.Parameters.AddWithValue("@profileType", profile.ProfileType.ToString());
            command.Parameters.AddWithValue("@lastLoginTime", profile.LastLoginTime);
            command.Parameters.AddWithValue("@isActive", profile.IsActive);
            command.Parameters.AddWithValue("@profileSize", profile.ProfileSizeBytes);
            command.Parameters.AddWithValue("@requiresBackup", profile.RequiresBackup);
            command.Parameters.AddWithValue("@backupPriority", profile.BackupPriority);

            await command.ExecuteNonQueryAsync(cancellationToken);

            await RecordSystemEventAsync("UserProfileUpdated", "Information",
                $"Updated profile for user {profile.UserName}", null, profile.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user profile for {UserId}", profile.UserId);
            throw;
        }
    }

    public async Task<IEnumerable<UserProfile>> GetActiveUserProfilesAsync(CancellationToken cancellationToken)
    {
        var profiles = new List<UserProfile>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId, UserName, DomainName, ProfilePath, ProfileType, 
                       LastLoginTime, IsActive, ProfileSizeBytes, RequiresBackup, 
                       BackupPriority, CreatedAt, UpdatedAt
                FROM UserProfiles
                WHERE IsActive = 1 AND RequiresBackup = 1
                ORDER BY BackupPriority DESC, LastLoginTime DESC";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                profiles.Add(ReadUserProfile(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active user profiles");
        }

        return profiles;
    }

    #endregion

    #region Migration State Management

    public async Task<IEnumerable<MigrationState>> GetActiveMigrationsAsync(CancellationToken cancellationToken)
    {
        var migrations = new List<MigrationState>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId, State, Status, Progress, StartedAt, LastUpdated, 
                       CompletedAt, Deadline, AttentionReason, DelayCount, IsBlocking
                FROM MigrationStates 
                WHERE Status = 'Active'";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                migrations.Add(ReadMigrationState(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active migrations");
        }

        return migrations;
    }

    public async Task<MigrationState?> GetMigrationStateAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            return await GetMigrationStateInternalAsync(userId, connection, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get migration state for user {UserId}", userId);
        }

        return null;
    }

    private async Task<MigrationState?> GetMigrationStateInternalAsync(string userId, SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = @"
            SELECT UserId, State, Status, Progress, StartedAt, LastUpdated, 
                   CompletedAt, Deadline, AttentionReason, DelayCount, IsBlocking
            FROM MigrationStates 
            WHERE UserId = @userId";
        command.Parameters.AddWithValue("@userId", userId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadMigrationState(reader);
        }

        return null;
    }

    public async Task UpdateMigrationStateAsync(MigrationState state, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await UpdateMigrationStateInternalAsync(state, connection, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update migration state for user {UserId}", state.UserId);
            throw;
        }
    }

    private async Task UpdateMigrationStateInternalAsync(MigrationState state, SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        if (transaction != null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = @"
            INSERT OR REPLACE INTO MigrationStates 
            (UserId, State, Status, Progress, StartedAt, LastUpdated, CompletedAt,
             Deadline, AttentionReason, DelayCount, IsBlocking) 
            VALUES (@userId, @state, @status, @progress, @startedAt, CURRENT_TIMESTAMP, 
                    @completedAt, @deadline, @attentionReason, @delayCount, @isBlocking)";

        command.Parameters.AddWithValue("@userId", state.UserId);
        command.Parameters.AddWithValue("@state", state.State.ToString());
        command.Parameters.AddWithValue("@status", state.Status);
        command.Parameters.AddWithValue("@progress", state.Progress);
        command.Parameters.AddWithValue("@startedAt", state.StartedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@completedAt", state.CompletedAt ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@deadline", state.Deadline ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@attentionReason", state.AttentionReason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@delayCount", state.DelayCount);
        command.Parameters.AddWithValue("@isBlocking", state.IsBlocking);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> TransitionStateAsync(string userId, MigrationStateType newState, string reason, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        try
        {
            // Get current state
            var currentState = await GetMigrationStateInternalAsync(userId, connection, transaction, cancellationToken);
            var oldStateValue = currentState?.State.ToString() ?? "NotStarted";

            // Create state if it doesn't exist
            if (currentState == null)
            {
                currentState = new MigrationState
                {
                    UserId = userId,
                    State = MigrationStateType.NotStarted,
                    StartedAt = DateTime.UtcNow
                };
            }

            // Validate the transition
            var validationResult = StateTransitionValidator.ValidateStateTransition(currentState, newState, reason);
            if (!validationResult.IsValid)
            {
                var errorMessage = string.Join("; ", validationResult.Errors);
                _logger.LogWarning("Invalid state transition for user {UserId}: {Errors}", userId, errorMessage);

                // Don't record system event within transaction to avoid connection conflicts
                transaction.Rollback();

                // Record event outside of transaction
                await RecordSystemEventAsync("InvalidStateTransition", "Warning",
                    $"Attempted invalid transition from {currentState.State} to {newState}",
                    errorMessage, userId, cancellationToken);

                return false;
            }

            // Update migration state
            currentState.State = newState;
            currentState.LastUpdated = DateTime.UtcNow;

            // Update state-specific fields
            switch (newState)
            {
                case MigrationStateType.Initializing:
                    if (!currentState.StartedAt.HasValue)
                    {
                        currentState.StartedAt = DateTime.UtcNow;
                    }

                    break;

                case MigrationStateType.BackupCompleted:
                case MigrationStateType.ReadyForReset:
                    currentState.CompletedAt = DateTime.UtcNow;
                    currentState.Progress = 100;
                    break;

                case MigrationStateType.Failed:
                case MigrationStateType.Escalated:
                    currentState.AttentionReason = reason;
                    break;
            }

            await UpdateMigrationStateInternalAsync(currentState, connection, transaction, cancellationToken);

            // Record state history
            using var historyCommand = connection.CreateCommand();
            historyCommand.Transaction = transaction;
            historyCommand.CommandText = @"
                INSERT INTO StateHistory (UserId, OldState, NewState, Reason, ChangedBy, Details)
                VALUES (@userId, @oldState, @newState, @reason, 'SYSTEM', @details)";

            historyCommand.Parameters.AddWithValue("@userId", userId);
            historyCommand.Parameters.AddWithValue("@oldState", oldStateValue);
            historyCommand.Parameters.AddWithValue("@newState", newState.ToString());
            historyCommand.Parameters.AddWithValue("@reason", reason);
            historyCommand.Parameters.AddWithValue("@details", $"Transition from {oldStateValue} to {newState}");

            await historyCommand.ExecuteNonQueryAsync(cancellationToken);

            transaction.Commit();

            _logger.LogInformation("User {UserId} transitioned from {OldState} to {NewState}: {Reason}",
                userId, oldStateValue, newState, reason);

            return true;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Failed to transition state for user {UserId}", userId);
            throw;
        }
    }

    #endregion

    #region Backup Operation Tracking

    public async Task<string> CreateBackupOperationAsync(BackupOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            operation.OperationId = Guid.NewGuid().ToString();

            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO BackupOperations 
                (UserId, OperationId, ProviderName, Category, Status, StartedAt, Progress,
                 BytesTotal, BytesTransferred, ItemsTotal, ItemsCompleted, RetryCount)
                VALUES (@userId, @operationId, @providerName, @category, @status, @startedAt, 
                        @progress, @bytesTotal, @bytesTransferred, @itemsTotal, @itemsCompleted, @retryCount)";

            command.Parameters.AddWithValue("@userId", operation.UserId);
            command.Parameters.AddWithValue("@operationId", operation.OperationId);
            command.Parameters.AddWithValue("@providerName", operation.ProviderName);
            command.Parameters.AddWithValue("@category", operation.Category);
            command.Parameters.AddWithValue("@status", operation.Status.ToString());
            command.Parameters.AddWithValue("@startedAt", operation.StartedAt);
            command.Parameters.AddWithValue("@progress", operation.Progress);
            command.Parameters.AddWithValue("@bytesTotal", operation.BytesTotal);
            command.Parameters.AddWithValue("@bytesTransferred", operation.BytesTransferred);
            command.Parameters.AddWithValue("@itemsTotal", operation.ItemsTotal);
            command.Parameters.AddWithValue("@itemsCompleted", operation.ItemsCompleted);
            command.Parameters.AddWithValue("@retryCount", operation.RetryCount);

            await command.ExecuteNonQueryAsync(cancellationToken);

            _logger.LogInformation("Created backup operation {OperationId} for user {UserId}",
                operation.OperationId, operation.UserId);

            return operation.OperationId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup operation for user {UserId}", operation.UserId);
            throw;
        }
    }

    public async Task UpdateBackupOperationAsync(BackupOperation operation, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE BackupOperations 
                SET Status = @status, Progress = @progress, CompletedAt = @completedAt,
                    BytesTransferred = @bytesTransferred, ItemsCompleted = @itemsCompleted,
                    ErrorCode = @errorCode, ErrorMessage = @errorMessage,
                    RetryCount = @retryCount, ManifestPath = @manifestPath
                WHERE OperationId = @operationId";

            command.Parameters.AddWithValue("@operationId", operation.OperationId);
            command.Parameters.AddWithValue("@status", operation.Status.ToString());
            command.Parameters.AddWithValue("@progress", operation.Progress);
            command.Parameters.AddWithValue("@completedAt", operation.CompletedAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@bytesTransferred", operation.BytesTransferred);
            command.Parameters.AddWithValue("@itemsCompleted", operation.ItemsCompleted);
            command.Parameters.AddWithValue("@errorCode", operation.ErrorCode ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@errorMessage", operation.ErrorMessage ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@retryCount", operation.RetryCount);
            command.Parameters.AddWithValue("@manifestPath", operation.ManifestPath ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update backup operation {OperationId}", operation.OperationId);
            throw;
        }
    }

    public async Task<BackupOperation?> GetBackupOperationAsync(string operationId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OperationId, ProviderName, Category, Status, StartedAt,
                       CompletedAt, Progress, BytesTotal, BytesTransferred, ItemsTotal,
                       ItemsCompleted, ErrorCode, ErrorMessage, RetryCount, ManifestPath
                FROM BackupOperations
                WHERE OperationId = @operationId";
            command.Parameters.AddWithValue("@operationId", operationId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadBackupOperation(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get backup operation {OperationId}", operationId);
        }

        return null;
    }

    public async Task<IEnumerable<BackupOperation>> GetUserBackupOperationsAsync(string userId, CancellationToken cancellationToken)
    {
        var operations = new List<BackupOperation>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OperationId, ProviderName, Category, Status, StartedAt,
                       CompletedAt, Progress, BytesTotal, BytesTransferred, ItemsTotal,
                       ItemsCompleted, ErrorCode, ErrorMessage, RetryCount, ManifestPath
                FROM BackupOperations
                WHERE UserId = @userId
                ORDER BY StartedAt DESC";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                operations.Add(ReadBackupOperation(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get backup operations for user {UserId}", userId);
        }

        return operations;
    }

    public async Task RecordProviderResultAsync(ProviderResult result, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ProviderResults 
                (BackupOperationId, Category, Success, ItemCount, SizeMB, Duration, Details, Errors)
                VALUES (@operationId, @category, @success, @itemCount, @sizeMB, @duration, @details, @errors)";

            command.Parameters.AddWithValue("@operationId", result.BackupOperationId);
            command.Parameters.AddWithValue("@category", result.Category);
            command.Parameters.AddWithValue("@success", result.Success);
            command.Parameters.AddWithValue("@itemCount", result.ItemCount);
            command.Parameters.AddWithValue("@sizeMB", result.SizeMB);
            command.Parameters.AddWithValue("@duration", result.Duration);
            command.Parameters.AddWithValue("@details", result.Details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@errors", result.Errors ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record provider result");
            throw;
        }
    }

    #endregion

    #region OneDrive Sync Tracking

    public async Task UpdateOneDriveSyncStatusAsync(OneDriveSyncStatusRecord status, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO OneDriveSync 
                (UserId, IsInstalled, IsSignedIn, AccountEmail, SyncFolderPath, SyncStatus,
                 QuotaTotalMB, QuotaUsedMB, QuotaAvailableMB, LastSyncTime, LastSyncError,
                 ErrorCount, CheckedAt)
                VALUES (@userId, @isInstalled, @isSignedIn, @accountEmail, @syncFolderPath,
                        @syncStatus, @quotaTotal, @quotaUsed, @quotaAvailable, @lastSyncTime,
                        @lastSyncError, @errorCount, CURRENT_TIMESTAMP)";

            command.Parameters.AddWithValue("@userId", status.UserId);
            command.Parameters.AddWithValue("@isInstalled", status.IsInstalled);
            command.Parameters.AddWithValue("@isSignedIn", status.IsSignedIn);
            command.Parameters.AddWithValue("@accountEmail", status.AccountEmail ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@syncFolderPath", status.SyncFolderPath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@syncStatus", status.SyncStatus);
            command.Parameters.AddWithValue("@quotaTotal", status.QuotaTotalMB ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@quotaUsed", status.QuotaUsedMB ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@quotaAvailable", status.QuotaAvailableMB ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@lastSyncTime", status.LastSyncTime ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@lastSyncError", status.LastSyncError ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@errorCount", status.ErrorCount);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update OneDrive sync status for user {UserId}", status.UserId);
            throw;
        }
    }

    public async Task<OneDriveSyncStatusRecord?> GetOneDriveSyncStatusAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, IsInstalled, IsSignedIn, AccountEmail, SyncFolderPath,
                       SyncStatus, QuotaTotalMB, QuotaUsedMB, QuotaAvailableMB, LastSyncTime,
                       LastSyncError, ErrorCount, CheckedAt
                FROM OneDriveSync
                WHERE UserId = @userId
                ORDER BY CheckedAt DESC
                LIMIT 1";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadOneDriveSyncStatus(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive sync status for user {UserId}", userId);
        }

        return null;
    }

    public async Task<IEnumerable<OneDriveSyncStatusRecord>> GetUsersWithSyncErrorsAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<OneDriveSyncStatusRecord>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DISTINCT o1.Id, o1.UserId, o1.IsInstalled, o1.IsSignedIn, 
                       o1.AccountEmail, o1.SyncFolderPath, o1.SyncStatus, o1.QuotaTotalMB,
                       o1.QuotaUsedMB, o1.QuotaAvailableMB, o1.LastSyncTime, o1.LastSyncError,
                       o1.ErrorCount, o1.CheckedAt
                FROM OneDriveSync o1
                INNER JOIN (
                    SELECT UserId, MAX(CheckedAt) as MaxCheckedAt
                    FROM OneDriveSync
                    GROUP BY UserId
                ) o2 ON o1.UserId = o2.UserId AND o1.CheckedAt = o2.MaxCheckedAt
                WHERE o1.ErrorCount > 0 OR o1.SyncStatus = 'Error'";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(ReadOneDriveSyncStatus(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get users with sync errors");
        }

        return statuses;
    }

    #endregion

    #region OneDrive Status Management (New Detailed Tracking)

    public async Task SaveOneDriveStatusAsync(OneDriveStatusRecord status, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO OneDriveStatus 
                (UserId, IsInstalled, IsRunning, IsSignedIn, AccountEmail, PrimaryAccountId,
                 SyncFolder, SyncStatus, AvailableSpaceMB, UsedSpaceMB, HasSyncErrors,
                 ErrorDetails, LastChecked, UpdatedAt)
                VALUES (@userId, @isInstalled, @isRunning, @isSignedIn, @accountEmail, 
                        @primaryAccountId, @syncFolder, @syncStatus, @availableSpace, @usedSpace,
                        @hasSyncErrors, @errorDetails, @lastChecked, CURRENT_TIMESTAMP)
                ON CONFLICT (UserId) 
                DO UPDATE SET
                    IsInstalled = @isInstalled,
                    IsRunning = @isRunning,
                    IsSignedIn = @isSignedIn,
                    AccountEmail = @accountEmail,
                    PrimaryAccountId = @primaryAccountId,
                    SyncFolder = @syncFolder,
                    SyncStatus = @syncStatus,
                    AvailableSpaceMB = @availableSpace,
                    UsedSpaceMB = @usedSpace,
                    HasSyncErrors = @hasSyncErrors,
                    ErrorDetails = @errorDetails,
                    LastChecked = @lastChecked,
                    UpdatedAt = CURRENT_TIMESTAMP";

            command.Parameters.AddWithValue("@userId", status.UserId);
            command.Parameters.AddWithValue("@isInstalled", status.IsInstalled);
            command.Parameters.AddWithValue("@isRunning", status.IsRunning);
            command.Parameters.AddWithValue("@isSignedIn", status.IsSignedIn);
            command.Parameters.AddWithValue("@accountEmail", status.AccountEmail ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@primaryAccountId", status.PrimaryAccountId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@syncFolder", status.SyncFolder ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@syncStatus", status.SyncStatus);
            command.Parameters.AddWithValue("@availableSpace", status.AvailableSpaceMB ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@usedSpace", status.UsedSpaceMB ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@hasSyncErrors", status.HasSyncErrors);
            command.Parameters.AddWithValue("@errorDetails", status.ErrorDetails ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@lastChecked", status.LastChecked);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OneDrive status for user {UserId}", status.UserId);
            throw;
        }
    }

    public async Task<OneDriveStatusRecord?> GetOneDriveStatusAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, IsInstalled, IsRunning, IsSignedIn, AccountEmail,
                       PrimaryAccountId, SyncFolder, SyncStatus, AvailableSpaceMB, UsedSpaceMB,
                       HasSyncErrors, ErrorDetails, LastChecked, CreatedAt, UpdatedAt
                FROM OneDriveStatus
                WHERE UserId = @userId";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new OneDriveStatusRecord
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    IsInstalled = reader.GetBoolean(2),
                    IsRunning = reader.GetBoolean(3),
                    IsSignedIn = reader.GetBoolean(4),
                    AccountEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
                    PrimaryAccountId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SyncFolder = reader.IsDBNull(7) ? null : reader.GetString(7),
                    SyncStatus = reader.GetString(8),
                    AvailableSpaceMB = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    UsedSpaceMB = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    HasSyncErrors = reader.GetBoolean(11),
                    ErrorDetails = reader.IsDBNull(12) ? null : reader.GetString(12),
                    LastChecked = reader.GetDateTime(13),
                    CreatedAt = reader.GetDateTime(14),
                    UpdatedAt = reader.GetDateTime(15)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive status for user {UserId}", userId);
        }

        return null;
    }

    public async Task<IEnumerable<OneDriveStatusRecord>> GetAllOneDriveStatusesAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<OneDriveStatusRecord>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, IsInstalled, IsRunning, IsSignedIn, AccountEmail,
                       PrimaryAccountId, SyncFolder, SyncStatus, AvailableSpaceMB, UsedSpaceMB,
                       HasSyncErrors, ErrorDetails, LastChecked, CreatedAt, UpdatedAt
                FROM OneDriveStatus
                ORDER BY UserId";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(new OneDriveStatusRecord
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    IsInstalled = reader.GetBoolean(2),
                    IsRunning = reader.GetBoolean(3),
                    IsSignedIn = reader.GetBoolean(4),
                    AccountEmail = reader.IsDBNull(5) ? null : reader.GetString(5),
                    PrimaryAccountId = reader.IsDBNull(6) ? null : reader.GetString(6),
                    SyncFolder = reader.IsDBNull(7) ? null : reader.GetString(7),
                    SyncStatus = reader.GetString(8),
                    AvailableSpaceMB = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    UsedSpaceMB = reader.IsDBNull(10) ? null : reader.GetInt64(10),
                    HasSyncErrors = reader.GetBoolean(11),
                    ErrorDetails = reader.IsDBNull(12) ? null : reader.GetString(12),
                    LastChecked = reader.GetDateTime(13),
                    CreatedAt = reader.GetDateTime(14),
                    UpdatedAt = reader.GetDateTime(15)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all OneDrive statuses");
        }

        return statuses;
    }

    #endregion

    #region OneDrive Account Management

    public async Task SaveOneDriveAccountAsync(OneDriveAccount account, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO OneDriveAccounts 
                (UserId, AccountId, Email, DisplayName, UserFolder, IsPrimary, LastSyncTime, UpdatedAt)
                VALUES (@userId, @accountId, @email, @displayName, @userFolder, @isPrimary, 
                        @lastSyncTime, CURRENT_TIMESTAMP)
                ON CONFLICT (UserId, AccountId) 
                DO UPDATE SET
                    Email = @email,
                    DisplayName = @displayName,
                    UserFolder = @userFolder,
                    IsPrimary = @isPrimary,
                    LastSyncTime = @lastSyncTime,
                    UpdatedAt = CURRENT_TIMESTAMP";

            command.Parameters.AddWithValue("@userId", account.UserId);
            command.Parameters.AddWithValue("@accountId", account.AccountId);
            command.Parameters.AddWithValue("@email", account.Email);
            command.Parameters.AddWithValue("@displayName", account.DisplayName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@userFolder", account.UserFolder);
            command.Parameters.AddWithValue("@isPrimary", account.IsPrimary);
            command.Parameters.AddWithValue("@lastSyncTime", account.LastSyncTime ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OneDrive account for user {UserId}", account.UserId);
            throw;
        }
    }

    public async Task<IEnumerable<OneDriveAccount>> GetOneDriveAccountsAsync(string userId, CancellationToken cancellationToken)
    {
        var accounts = new List<OneDriveAccount>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, Email, DisplayName, UserFolder, 
                       IsPrimary, LastSyncTime, CreatedAt, UpdatedAt
                FROM OneDriveAccounts
                WHERE UserId = @userId
                ORDER BY IsPrimary DESC, Email";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                accounts.Add(new OneDriveAccount
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    AccountId = reader.GetString(2),
                    Email = reader.GetString(3),
                    DisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    UserFolder = reader.GetString(5),
                    IsPrimary = reader.GetBoolean(6),
                    LastSyncTime = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    CreatedAt = reader.GetDateTime(8),
                    UpdatedAt = reader.GetDateTime(9)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive accounts for user {UserId}", userId);
        }

        return accounts;
    }

    public async Task<OneDriveAccount?> GetPrimaryOneDriveAccountAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, Email, DisplayName, UserFolder, 
                       IsPrimary, LastSyncTime, CreatedAt, UpdatedAt
                FROM OneDriveAccounts
                WHERE UserId = @userId AND IsPrimary = 1
                LIMIT 1";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return new OneDriveAccount
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    AccountId = reader.GetString(2),
                    Email = reader.GetString(3),
                    DisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                    UserFolder = reader.GetString(5),
                    IsPrimary = reader.GetBoolean(6),
                    LastSyncTime = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    CreatedAt = reader.GetDateTime(8),
                    UpdatedAt = reader.GetDateTime(9)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get primary OneDrive account for user {UserId}", userId);
        }

        return null;
    }

    public async Task DeleteOneDriveAccountAsync(string userId, string accountId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM OneDriveAccounts 
                WHERE UserId = @userId AND AccountId = @accountId";
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@accountId", accountId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OneDrive account {AccountId} for user {UserId}", accountId, userId);
            throw;
        }
    }

    #endregion

    #region OneDrive Synced Folder Management

    public async Task SaveOneDriveSyncedFolderAsync(OneDriveSyncedFolder folder, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            if (folder.Id == 0)
            {
                command.CommandText = @"
                    INSERT INTO OneDriveSyncedFolders 
                    (UserId, AccountId, LocalPath, RemotePath, FolderType, DisplayName,
                     SharePointSiteUrl, LibraryName, SizeBytes, FileCount, IsSyncing,
                     HasErrors, LastSyncCheck, UpdatedAt)
                    VALUES (@userId, @accountId, @localPath, @remotePath, @folderType, @displayName,
                            @sharePointSiteUrl, @libraryName, @sizeBytes, @fileCount, @isSyncing,
                            @hasErrors, @lastSyncCheck, CURRENT_TIMESTAMP)";
            }
            else
            {
                command.CommandText = @"
                    UPDATE OneDriveSyncedFolders 
                    SET AccountId = @accountId,
                        LocalPath = @localPath,
                        RemotePath = @remotePath,
                        FolderType = @folderType,
                        DisplayName = @displayName,
                        SharePointSiteUrl = @sharePointSiteUrl,
                        LibraryName = @libraryName,
                        SizeBytes = @sizeBytes,
                        FileCount = @fileCount,
                        IsSyncing = @isSyncing,
                        HasErrors = @hasErrors,
                        LastSyncCheck = @lastSyncCheck,
                        UpdatedAt = CURRENT_TIMESTAMP
                    WHERE Id = @id";

                command.Parameters.AddWithValue("@id", folder.Id);
            }

            command.Parameters.AddWithValue("@userId", folder.UserId);
            command.Parameters.AddWithValue("@accountId", folder.AccountId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@localPath", folder.LocalPath);
            command.Parameters.AddWithValue("@remotePath", folder.RemotePath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@folderType", folder.FolderType.ToString());
            command.Parameters.AddWithValue("@displayName", folder.DisplayName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sharePointSiteUrl", folder.SharePointSiteUrl ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@libraryName", folder.LibraryName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@sizeBytes", folder.SizeBytes ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@fileCount", folder.FileCount ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@isSyncing", folder.IsSyncing);
            command.Parameters.AddWithValue("@hasErrors", folder.HasErrors);
            command.Parameters.AddWithValue("@lastSyncCheck", folder.LastSyncCheck ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OneDrive synced folder for user {UserId}", folder.UserId);
            throw;
        }
    }

    public async Task<IEnumerable<OneDriveSyncedFolder>> GetOneDriveSyncedFoldersAsync(string userId, CancellationToken cancellationToken)
    {
        var folders = new List<OneDriveSyncedFolder>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, LocalPath, RemotePath, FolderType, DisplayName,
                       SharePointSiteUrl, LibraryName, SizeBytes, FileCount, IsSyncing,
                       HasErrors, LastSyncCheck, CreatedAt, UpdatedAt
                FROM OneDriveSyncedFolders
                WHERE UserId = @userId
                ORDER BY FolderType, LocalPath";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                folders.Add(ReadSyncedFolder(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive synced folders for user {UserId}", userId);
        }

        return folders;
    }

    public async Task<IEnumerable<OneDriveSyncedFolder>> GetOneDriveSyncedFoldersForAccountAsync(
        string userId, string accountId, CancellationToken cancellationToken)
    {
        var folders = new List<OneDriveSyncedFolder>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, LocalPath, RemotePath, FolderType, DisplayName,
                       SharePointSiteUrl, LibraryName, SizeBytes, FileCount, IsSyncing,
                       HasErrors, LastSyncCheck, CreatedAt, UpdatedAt
                FROM OneDriveSyncedFolders
                WHERE UserId = @userId AND AccountId = @accountId
                ORDER BY FolderType, LocalPath";
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@accountId", accountId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                folders.Add(ReadSyncedFolder(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive synced folders for account {AccountId}", accountId);
        }

        return folders;
    }

    public async Task DeleteOneDriveSyncedFolderAsync(int folderId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM OneDriveSyncedFolders WHERE Id = @id";
            command.Parameters.AddWithValue("@id", folderId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete OneDrive synced folder {FolderId}", folderId);
            throw;
        }
    }

    public async Task UpdateSyncedFolderStatusAsync(int folderId, bool isSyncing, bool hasErrors, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE OneDriveSyncedFolders 
                SET IsSyncing = @isSyncing,
                    HasErrors = @hasErrors,
                    LastSyncCheck = CURRENT_TIMESTAMP,
                    UpdatedAt = CURRENT_TIMESTAMP
                WHERE Id = @id";
            command.Parameters.AddWithValue("@id", folderId);
            command.Parameters.AddWithValue("@isSyncing", isSyncing);
            command.Parameters.AddWithValue("@hasErrors", hasErrors);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update sync status for folder {FolderId}", folderId);
            throw;
        }
    }

    private static OneDriveSyncedFolder ReadSyncedFolder(SqliteDataReader reader)
    {
        return new OneDriveSyncedFolder
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            AccountId = reader.IsDBNull(2) ? null : reader.GetString(2),
            LocalPath = reader.GetString(3),
            RemotePath = reader.IsDBNull(4) ? null : reader.GetString(4),
            FolderType = Enum.Parse<OneDriveFolderType>(reader.GetString(5)),
            DisplayName = reader.IsDBNull(6) ? null : reader.GetString(6),
            SharePointSiteUrl = reader.IsDBNull(7) ? null : reader.GetString(7),
            LibraryName = reader.IsDBNull(8) ? null : reader.GetString(8),
            SizeBytes = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            FileCount = reader.IsDBNull(10) ? null : reader.GetInt32(10),
            IsSyncing = reader.GetBoolean(11),
            HasErrors = reader.GetBoolean(12),
            LastSyncCheck = reader.IsDBNull(13) ? null : reader.GetDateTime(13),
            CreatedAt = reader.GetDateTime(14),
            UpdatedAt = reader.GetDateTime(15)
        };
    }

    #endregion

    #region Known Folder Move Management

    public async Task SaveKnownFolderMoveStatusAsync(KnownFolderMoveStatus status, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO KnownFolderMoveStatus 
                (UserId, AccountId, IsEnabled, DesktopRedirected, DesktopPath,
                 DocumentsRedirected, DocumentsPath, PicturesRedirected, PicturesPath,
                 ConfigurationSource, UpdatedAt)
                VALUES (@userId, @accountId, @isEnabled, @desktopRedirected, @desktopPath,
                        @documentsRedirected, @documentsPath, @picturesRedirected, @picturesPath,
                        @configSource, CURRENT_TIMESTAMP)
                ON CONFLICT (UserId, AccountId) 
                DO UPDATE SET
                    IsEnabled = @isEnabled,
                    DesktopRedirected = @desktopRedirected,
                    DesktopPath = @desktopPath,
                    DocumentsRedirected = @documentsRedirected,
                    DocumentsPath = @documentsPath,
                    PicturesRedirected = @picturesRedirected,
                    PicturesPath = @picturesPath,
                    ConfigurationSource = @configSource,
                    UpdatedAt = CURRENT_TIMESTAMP";

            command.Parameters.AddWithValue("@userId", status.UserId);
            command.Parameters.AddWithValue("@accountId", status.AccountId);
            command.Parameters.AddWithValue("@isEnabled", status.IsEnabled);
            command.Parameters.AddWithValue("@desktopRedirected", status.DesktopRedirected);
            command.Parameters.AddWithValue("@desktopPath", status.DesktopPath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@documentsRedirected", status.DocumentsRedirected);
            command.Parameters.AddWithValue("@documentsPath", status.DocumentsPath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@picturesRedirected", status.PicturesRedirected);
            command.Parameters.AddWithValue("@picturesPath", status.PicturesPath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@configSource", status.ConfigurationSource ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save Known Folder Move status for user {UserId}", status.UserId);
            throw;
        }
    }

    public async Task<KnownFolderMoveStatus?> GetKnownFolderMoveStatusAsync(string userId, string accountId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, IsEnabled, DesktopRedirected, DesktopPath,
                       DocumentsRedirected, DocumentsPath, PicturesRedirected, PicturesPath,
                       ConfigurationSource, CreatedAt, UpdatedAt
                FROM KnownFolderMoveStatus
                WHERE UserId = @userId AND AccountId = @accountId";
            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@accountId", accountId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadKnownFolderMoveStatus(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Known Folder Move status for user {UserId}", userId);
        }

        return null;
    }

    public async Task<IEnumerable<KnownFolderMoveStatus>> GetAllKnownFolderMoveStatusesAsync(string userId, CancellationToken cancellationToken)
    {
        var statuses = new List<KnownFolderMoveStatus>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, AccountId, IsEnabled, DesktopRedirected, DesktopPath,
                       DocumentsRedirected, DocumentsPath, PicturesRedirected, PicturesPath,
                       ConfigurationSource, CreatedAt, UpdatedAt
                FROM KnownFolderMoveStatus
                WHERE UserId = @userId";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                statuses.Add(ReadKnownFolderMoveStatus(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all Known Folder Move statuses for user {UserId}", userId);
        }

        return statuses;
    }

    private static KnownFolderMoveStatus ReadKnownFolderMoveStatus(SqliteDataReader reader)
    {
        return new KnownFolderMoveStatus
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            AccountId = reader.GetString(2),
            IsEnabled = reader.GetBoolean(3),
            DesktopRedirected = reader.GetBoolean(4),
            DesktopPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            DocumentsRedirected = reader.GetBoolean(6),
            DocumentsPath = reader.IsDBNull(7) ? null : reader.GetString(7),
            PicturesRedirected = reader.GetBoolean(8),
            PicturesPath = reader.IsDBNull(9) ? null : reader.GetString(9),
            ConfigurationSource = reader.IsDBNull(10) ? null : reader.GetString(10),
            CreatedAt = reader.GetDateTime(11),
            UpdatedAt = reader.GetDateTime(12)
        };
    }

    #endregion

    #region OneDrive Sync Error Management

    public async Task SaveOneDriveSyncErrorAsync(OneDriveSyncError error, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO OneDriveSyncErrors 
                (UserId, FolderPath, FilePath, ErrorMessage, ErrorCode, IsRecoverable,
                 AttemptedRecovery, RecoveryResult, ErrorTime, ResolvedTime)
                VALUES (@userId, @folderPath, @filePath, @errorMessage, @errorCode, @isRecoverable,
                        @attemptedRecovery, @recoveryResult, @errorTime, @resolvedTime)";

            command.Parameters.AddWithValue("@userId", error.UserId);
            command.Parameters.AddWithValue("@folderPath", error.FolderPath);
            command.Parameters.AddWithValue("@filePath", error.FilePath ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@errorMessage", error.ErrorMessage);
            command.Parameters.AddWithValue("@errorCode", error.ErrorCode ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@isRecoverable", error.IsRecoverable);
            command.Parameters.AddWithValue("@attemptedRecovery", error.AttemptedRecovery);
            command.Parameters.AddWithValue("@recoveryResult", error.RecoveryResult ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@errorTime", error.ErrorTime);
            command.Parameters.AddWithValue("@resolvedTime", error.ResolvedTime ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save OneDrive sync error for user {UserId}", error.UserId);
            throw;
        }
    }

    public async Task<IEnumerable<OneDriveSyncError>> GetOneDriveSyncErrorsAsync(string userId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var errors = new List<OneDriveSyncError>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, FolderPath, FilePath, ErrorMessage, ErrorCode,
                       IsRecoverable, AttemptedRecovery, RecoveryResult, ErrorTime,
                       ResolvedTime, CreatedAt
                FROM OneDriveSyncErrors
                WHERE UserId = @userId
                ORDER BY ErrorTime DESC";

            if (limit.HasValue)
            {
                command.CommandText += " LIMIT @limit";
                command.Parameters.AddWithValue("@limit", limit.Value);
            }

            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                errors.Add(ReadSyncError(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive sync errors for user {UserId}", userId);
        }

        return errors;
    }

    public async Task<IEnumerable<OneDriveSyncError>> GetUnresolvedSyncErrorsAsync(string userId, CancellationToken cancellationToken)
    {
        var errors = new List<OneDriveSyncError>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, FolderPath, FilePath, ErrorMessage, ErrorCode,
                       IsRecoverable, AttemptedRecovery, RecoveryResult, ErrorTime,
                       ResolvedTime, CreatedAt
                FROM OneDriveSyncErrors
                WHERE UserId = @userId AND ResolvedTime IS NULL
                ORDER BY ErrorTime DESC";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                errors.Add(ReadSyncError(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unresolved sync errors for user {UserId}", userId);
        }

        return errors;
    }

    public async Task MarkSyncErrorResolvedAsync(int errorId, string? recoveryResult = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE OneDriveSyncErrors 
                SET ResolvedTime = CURRENT_TIMESTAMP,
                    RecoveryResult = COALESCE(@recoveryResult, RecoveryResult)
                WHERE Id = @id";
            command.Parameters.AddWithValue("@id", errorId);
            command.Parameters.AddWithValue("@recoveryResult", recoveryResult ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark sync error {ErrorId} as resolved", errorId);
            throw;
        }
    }

    public async Task<int> GetActiveSyncErrorCountAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM OneDriveSyncErrors
                WHERE UserId = @userId AND ResolvedTime IS NULL";
            command.Parameters.AddWithValue("@userId", userId);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active sync error count for user {UserId}", userId);
            return 0;
        }
    }

    private static OneDriveSyncError ReadSyncError(SqliteDataReader reader)
    {
        return new OneDriveSyncError
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            FolderPath = reader.GetString(2),
            FilePath = reader.IsDBNull(3) ? null : reader.GetString(3),
            ErrorMessage = reader.GetString(4),
            ErrorCode = reader.IsDBNull(5) ? null : reader.GetString(5),
            IsRecoverable = reader.GetBoolean(6),
            AttemptedRecovery = reader.GetBoolean(7),
            RecoveryResult = reader.IsDBNull(8) ? null : reader.GetString(8),
            ErrorTime = reader.GetDateTime(9),
            ResolvedTime = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            CreatedAt = reader.GetDateTime(11)
        };
    }

    #endregion

    #region OneDrive Aggregated Queries

    public async Task<OneDriveUserSummary> GetOneDriveUserSummaryAsync(string userId, CancellationToken cancellationToken)
    {
        var summary = new OneDriveUserSummary { UserId = userId };

        summary.Status = await GetOneDriveStatusAsync(userId, cancellationToken);
        summary.Accounts = (await GetOneDriveAccountsAsync(userId, cancellationToken)).ToList();
        summary.SyncedFolders = (await GetOneDriveSyncedFoldersAsync(userId, cancellationToken)).ToList();

        if (summary.Accounts.Any())
        {
            var primaryAccount = summary.Accounts.FirstOrDefault(a => a.IsPrimary);
            if (primaryAccount != null)
            {
                summary.KnownFolderStatus = await GetKnownFolderMoveStatusAsync(userId, primaryAccount.AccountId, cancellationToken);
            }
        }

        summary.RecentErrors = (await GetOneDriveSyncErrorsAsync(userId, 10, cancellationToken)).ToList();

        return summary;
    }

    public async Task<IEnumerable<string>> GetUsersWithOneDriveErrorsAsync(CancellationToken cancellationToken)
    {
        var userIds = new List<string>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DISTINCT UserId FROM (
                    SELECT UserId FROM OneDriveStatus WHERE HasSyncErrors = 1
                    UNION
                    SELECT UserId FROM OneDriveSyncedFolders WHERE HasErrors = 1
                    UNION
                    SELECT UserId FROM OneDriveSyncErrors WHERE ResolvedTime IS NULL
                )";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                userIds.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get users with OneDrive errors");
        }

        return userIds;
    }

    public async Task<Dictionary<string, long>> GetOneDriveStorageUsageAsync(CancellationToken cancellationToken)
    {
        var usage = new Dictionary<string, long>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT UserId, SUM(SizeBytes) as TotalBytes
                FROM OneDriveSyncedFolders
                WHERE SizeBytes IS NOT NULL
                GROUP BY UserId";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                usage[reader.GetString(0)] = reader.GetInt64(1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OneDrive storage usage");
        }

        return usage;
    }

    public async Task<bool> IsUserReadyForOneDriveBackupAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            var status = await GetOneDriveStatusAsync(userId, cancellationToken);
            if (status == null || !status.IsInstalled || !status.IsSignedIn || status.HasSyncErrors)
            {
                return false;
            }

            var errorCount = await GetActiveSyncErrorCountAsync(userId, cancellationToken);
            if (errorCount > 0)
            {
                return false;
            }

            var folders = await GetOneDriveSyncedFoldersAsync(userId, cancellationToken);
            if (folders.Any(f => f.HasErrors))
            {
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if user {UserId} is ready for OneDrive backup", userId);
            return false;
        }
    }

    #endregion

    #region IT Escalation Management

    public async Task<int> CreateEscalationAsync(ITEscalation escalation, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ITEscalations 
                (UserId, TriggerType, TriggerReason, Details, TicketNumber, Status, AutoTriggered)
                VALUES (@userId, @triggerType, @triggerReason, @details, @ticketNumber, @status, @autoTriggered);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@userId", escalation.UserId ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@triggerType", escalation.TriggerType.ToString());
            command.Parameters.AddWithValue("@triggerReason", escalation.TriggerReason);
            command.Parameters.AddWithValue("@details", escalation.Details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ticketNumber", escalation.TicketNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@status", escalation.Status);
            command.Parameters.AddWithValue("@autoTriggered", escalation.AutoTriggered);

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

            await RecordSystemEventAsync("ITEscalation", "Warning",
                $"IT escalation created: {escalation.TriggerType}", escalation.Details, escalation.UserId, cancellationToken);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create IT escalation");
            throw;
        }
    }

    public async Task UpdateEscalationAsync(ITEscalation escalation, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ITEscalations 
                SET Status = @status, TicketNumber = @ticketNumber, ResolvedAt = @resolvedAt,
                    ResolutionNotes = @resolutionNotes
                WHERE Id = @id";

            command.Parameters.AddWithValue("@id", escalation.Id);
            command.Parameters.AddWithValue("@status", escalation.Status);
            command.Parameters.AddWithValue("@ticketNumber", escalation.TicketNumber ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@resolvedAt", escalation.ResolvedAt ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@resolutionNotes", escalation.ResolutionNotes ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update IT escalation {Id}", escalation.Id);
            throw;
        }
    }

    public async Task<IEnumerable<ITEscalation>> GetOpenEscalationsAsync(CancellationToken cancellationToken)
    {
        var escalations = new List<ITEscalation>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, TriggerType, TriggerReason, Details, TicketNumber,
                       Status, CreatedAt, ResolvedAt, ResolutionNotes, AutoTriggered
                FROM ITEscalations
                WHERE Status = 'Open'
                ORDER BY CreatedAt DESC";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                escalations.Add(ReadITEscalation(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get open escalations");
        }

        return escalations;
    }

    public async Task<IEnumerable<ITEscalation>> GetUserEscalationsAsync(string userId, CancellationToken cancellationToken)
    {
        var escalations = new List<ITEscalation>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, TriggerType, TriggerReason, Details, TicketNumber,
                       Status, CreatedAt, ResolvedAt, ResolutionNotes, AutoTriggered
                FROM ITEscalations
                WHERE UserId = @userId
                ORDER BY CreatedAt DESC";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                escalations.Add(ReadITEscalation(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get escalations for user {UserId}", userId);
        }

        return escalations;
    }

    #endregion

    #region Delay Request Management

    public async Task<int> CreateDelayRequestAsync(DelayRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO DelayRequests 
                (UserId, RequestedDelayHours, Reason, Status)
                VALUES (@userId, @delayHours, @reason, @status);
                SELECT last_insert_rowid();";

            command.Parameters.AddWithValue("@userId", request.UserId);
            command.Parameters.AddWithValue("@delayHours", request.RequestedDelayHours);
            command.Parameters.AddWithValue("@reason", request.Reason);
            command.Parameters.AddWithValue("@status", request.Status);

            var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));

            _logger.LogInformation("Delay request created for user {UserId}: {Hours} hours",
                request.UserId, request.RequestedDelayHours);

            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create delay request");
            throw;
        }
    }

    public async Task ApproveDelayRequestAsync(int requestId, DateTime newDeadline, CancellationToken cancellationToken)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var transaction = connection.BeginTransaction();
        try
        {
            // Update delay request
            using var updateCommand = connection.CreateCommand();
            updateCommand.Transaction = transaction;
            updateCommand.CommandText = @"
                UPDATE DelayRequests 
                SET Status = 'Approved', ApprovedAt = CURRENT_TIMESTAMP, NewDeadline = @newDeadline
                WHERE Id = @id";

            updateCommand.Parameters.AddWithValue("@id", requestId);
            updateCommand.Parameters.AddWithValue("@newDeadline", newDeadline);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);

            // Get user ID and update migration state
            using var selectCommand = connection.CreateCommand();
            selectCommand.Transaction = transaction;
            selectCommand.CommandText = "SELECT UserId FROM DelayRequests WHERE Id = @id";
            selectCommand.Parameters.AddWithValue("@id", requestId);

            var userId = (string?)await selectCommand.ExecuteScalarAsync(cancellationToken);
            if (userId != null)
            {
                using var updateStateCommand = connection.CreateCommand();
                updateStateCommand.Transaction = transaction;
                updateStateCommand.CommandText = @"
                    UPDATE MigrationStates 
                    SET Deadline = @newDeadline, DelayCount = DelayCount + 1
                    WHERE UserId = @userId";

                updateStateCommand.Parameters.AddWithValue("@userId", userId);
                updateStateCommand.Parameters.AddWithValue("@newDeadline", newDeadline);
                await updateStateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            transaction.Commit();

            _logger.LogInformation("Approved delay request {RequestId} with new deadline {Deadline}",
                requestId, newDeadline);
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            _logger.LogError(ex, "Failed to approve delay request {RequestId}", requestId);
            throw;
        }
    }

    public async Task<IEnumerable<DelayRequest>> GetPendingDelayRequestsAsync(CancellationToken cancellationToken)
    {
        var requests = new List<DelayRequest>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, RequestedAt, RequestedDelayHours, Reason, Status,
                       ApprovedAt, NewDeadline
                FROM DelayRequests
                WHERE Status = 'Pending'
                ORDER BY RequestedAt";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                requests.Add(ReadDelayRequest(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending delay requests");
        }

        return requests;
    }

    public async Task<int> GetUserDelayCountAsync(string userId, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM DelayRequests
                WHERE UserId = @userId AND Status = 'Approved'";
            command.Parameters.AddWithValue("@userId", userId);

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get delay count for user {UserId}", userId);
            return 0;
        }
    }

    #endregion

    #region State History and Audit

    public async Task<IEnumerable<StateHistoryEntry>> GetStateHistoryAsync(string userId, CancellationToken cancellationToken)
    {
        var history = new List<StateHistoryEntry>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OldState, NewState, Reason, ChangedBy, ChangedAt, Details
                FROM StateHistory
                WHERE UserId = @userId
                ORDER BY ChangedAt DESC";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(new StateHistoryEntry
                {
                    Id = reader.GetInt32(0),
                    UserId = reader.GetString(1),
                    OldState = reader.IsDBNull(2) ? null : reader.GetString(2),
                    NewState = reader.GetString(3),
                    Reason = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ChangedBy = reader.GetString(5),
                    ChangedAt = reader.GetDateTime(6),
                    Details = reader.IsDBNull(7) ? null : reader.GetString(7)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state history for user {UserId}", userId);
        }

        return history;
    }

    public async Task RecordSystemEventAsync(string eventType, string severity, string message,
        string? details = null, string? userId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO SystemEvents (EventType, Severity, Source, Message, Details, UserId)
                VALUES (@eventType, @severity, 'StateManager', @message, @details, @userId)";

            command.Parameters.AddWithValue("@eventType", eventType);
            command.Parameters.AddWithValue("@severity", severity);
            command.Parameters.AddWithValue("@message", message);
            command.Parameters.AddWithValue("@details", details ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@userId", userId ?? (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record system event");
            // Don't throw - logging shouldn't break operations
        }
    }

    #endregion

    #region Classification Management

    public async Task SaveUserClassificationAsync(string userId, ProfileClassification classification, double confidence, string reason, string? ruleSetName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO UserClassifications 
                (UserId, Classification, ClassificationDate, Confidence, Reason, RuleSetName, RuleSetVersion, IsOverridden, ActivityScore)
                VALUES (@userId, @classification, @classificationDate, @confidence, @reason, @ruleSetName, @ruleSetVersion, @isOverridden, @activityScore)";

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@classification", classification.ToString());
            command.Parameters.AddWithValue("@classificationDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@confidence", confidence);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@ruleSetName", ruleSetName ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@ruleSetVersion", (object)DBNull.Value); // TODO: Add version tracking
            command.Parameters.AddWithValue("@isOverridden", false);
            command.Parameters.AddWithValue("@activityScore", (object)DBNull.Value); // TODO: Pass from caller

            await command.ExecuteNonQueryAsync(cancellationToken);

            await RecordSystemEventAsync("UserClassified", "Information",
                $"User {userId} classified as {classification}", reason, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save user classification for {UserId}", userId);
            throw;
        }
    }

    public async Task<UserClassificationRecord?> GetUserClassificationAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, Classification, ClassificationDate, Confidence, Reason, 
                       RuleSetName, RuleSetVersion, IsOverridden, ActivityScore, CreatedAt, UpdatedAt
                FROM UserClassifications
                WHERE UserId = @userId";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadUserClassificationRecord(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user classification for {UserId}", userId);
        }

        return null;
    }

    public async Task<IEnumerable<UserClassificationRecord>> GetAllClassificationsAsync(CancellationToken cancellationToken = default)
    {
        var classifications = new List<UserClassificationRecord>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, Classification, ClassificationDate, Confidence, Reason, 
                       RuleSetName, RuleSetVersion, IsOverridden, ActivityScore, CreatedAt, UpdatedAt
                FROM UserClassifications
                ORDER BY ClassificationDate DESC";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                classifications.Add(ReadUserClassificationRecord(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all user classifications");
        }

        return classifications;
    }

    public async Task SaveClassificationHistoryAsync(string userId, ProfileClassification? oldClassification, ProfileClassification newClassification, string reason, Dictionary<string, object>? activitySnapshot = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ClassificationHistory 
                (UserId, OldClassification, NewClassification, ChangeDate, Reason, ActivitySnapshot)
                VALUES (@userId, @oldClassification, @newClassification, @changeDate, @reason, @activitySnapshot)";

            command.Parameters.AddWithValue("@userId", userId);
            command.Parameters.AddWithValue("@oldClassification", oldClassification?.ToString() ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@newClassification", newClassification.ToString());
            command.Parameters.AddWithValue("@changeDate", DateTime.UtcNow);
            command.Parameters.AddWithValue("@reason", reason);
            command.Parameters.AddWithValue("@activitySnapshot",
                activitySnapshot != null ? JsonSerializer.Serialize(activitySnapshot) : (object)DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save classification history for {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<ClassificationHistoryEntry>> GetClassificationHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var history = new List<ClassificationHistoryEntry>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OldClassification, NewClassification, ChangeDate, Reason, ActivitySnapshot, CreatedAt
                FROM ClassificationHistory
                WHERE UserId = @userId
                ORDER BY ChangeDate DESC";

            if (limit.HasValue)
            {
                command.CommandText += " LIMIT @limit";
                command.Parameters.AddWithValue("@limit", limit.Value);
            }

            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(ReadClassificationHistoryEntry(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get classification history for {UserId}", userId);
        }

        return history;
    }

    public async Task SaveClassificationOverrideAsync(ClassificationOverride override_, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT OR REPLACE INTO ClassificationOverrides 
                (UserId, OverrideClassification, OverrideDate, OverrideBy, Reason, ExpiryDate, IsActive)
                VALUES (@userId, @overrideClassification, @overrideDate, @overrideBy, @reason, @expiryDate, @isActive)";

            command.Parameters.AddWithValue("@userId", override_.UserId);
            command.Parameters.AddWithValue("@overrideClassification", override_.OverrideClassification.ToString());
            command.Parameters.AddWithValue("@overrideDate", override_.OverrideDate);
            command.Parameters.AddWithValue("@overrideBy", override_.OverrideBy);
            command.Parameters.AddWithValue("@reason", override_.Reason);
            command.Parameters.AddWithValue("@expiryDate", override_.ExpiryDate ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@isActive", override_.IsActive);

            await command.ExecuteNonQueryAsync(cancellationToken);

            // Update the override ID if this was an insert
            if (override_.Id == 0)
            {
                // Get the last inserted ID
                using var idCommand = connection.CreateCommand();
                idCommand.CommandText = "SELECT last_insert_rowid()";
                var lastId = await idCommand.ExecuteScalarAsync(cancellationToken);
                override_.Id = Convert.ToInt32(lastId);
            }

            // Also mark the user classification as overridden
            using var updateCommand = connection.CreateCommand();
            updateCommand.CommandText = @"
                UPDATE UserClassifications 
                SET IsOverridden = 1 
                WHERE UserId = @userId";
            updateCommand.Parameters.AddWithValue("@userId", override_.UserId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save classification override for {UserId}", override_.UserId);
            throw;
        }
    }

    public async Task<ClassificationOverride?> GetClassificationOverrideAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OverrideClassification, OverrideDate, OverrideBy, Reason, ExpiryDate, IsActive
                FROM ClassificationOverrides
                WHERE UserId = @userId AND IsActive = 1";
            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return ReadClassificationOverride(reader);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get classification override for {UserId}", userId);
        }

        return null;
    }

    public async Task<IEnumerable<ClassificationOverride>> GetAllClassificationOverridesAsync(CancellationToken cancellationToken = default)
    {
        var overrides = new List<ClassificationOverride>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OverrideClassification, OverrideDate, OverrideBy, Reason, ExpiryDate, IsActive
                FROM ClassificationOverrides
                ORDER BY OverrideDate DESC";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                overrides.Add(ReadClassificationOverride(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all classification overrides");
        }

        return overrides;
    }

    public async Task ExpireClassificationOverrideAsync(int overrideId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                UPDATE ClassificationOverrides 
                SET IsActive = 0 
                WHERE Id = @id";
            command.Parameters.AddWithValue("@id", overrideId);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire classification override {OverrideId}", overrideId);
            throw;
        }
    }

    public async Task SaveClassificationOverrideHistoryAsync(ClassificationOverrideHistory history, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO ClassificationOverrideHistory 
                (UserId, OldClassification, NewClassification, ChangeDate, ChangedBy, Reason)
                VALUES (@userId, @oldClassification, @newClassification, @changeDate, @changedBy, @reason)";

            command.Parameters.AddWithValue("@userId", history.UserId);
            command.Parameters.AddWithValue("@oldClassification", history.OldClassification ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@newClassification", history.NewClassification ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("@changeDate", history.ChangeDate);
            command.Parameters.AddWithValue("@changedBy", history.ChangedBy);
            command.Parameters.AddWithValue("@reason", history.Reason);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save classification override history for {UserId}", history.UserId);
            throw;
        }
    }

    public async Task<IEnumerable<ClassificationOverrideHistory>> GetClassificationOverrideHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default)
    {
        var history = new List<ClassificationOverrideHistory>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT Id, UserId, OldClassification, NewClassification, ChangeDate, ChangedBy, Reason
                FROM ClassificationOverrideHistory
                WHERE UserId = @userId
                ORDER BY ChangeDate DESC";

            if (limit.HasValue)
            {
                command.CommandText += " LIMIT @limit";
                command.Parameters.AddWithValue("@limit", limit.Value);
            }

            command.Parameters.AddWithValue("@userId", userId);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                history.Add(ReadClassificationOverrideHistory(reader));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get classification override history for {UserId}", userId);
        }

        return history;
    }

    #endregion

    #region Aggregated Queries

    public async Task<bool> AreAllUsersReadyForResetAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM UserProfiles p
                LEFT JOIN MigrationStates m ON p.UserId = m.UserId
                WHERE p.IsActive = 1 AND p.RequiresBackup = 1
                AND (m.State IS NULL OR m.State NOT IN ('BackupCompleted', 'ReadyForReset'))";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check reset readiness");
            return false;
        }
    }

    public async Task<MigrationReadinessStatus> GetMigrationReadinessAsync(CancellationToken cancellationToken)
    {
        var status = new MigrationReadinessStatus();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get user counts
            using var countCommand = connection.CreateCommand();
            countCommand.CommandText = @"
                SELECT 
                    COUNT(*) as TotalUsers,
                    SUM(CASE WHEN p.IsActive = 1 AND p.RequiresBackup = 1 THEN 1 ELSE 0 END) as ActiveUsers,
                    SUM(CASE WHEN m.State IN ('BackupCompleted', 'ReadyForReset') THEN 1 ELSE 0 END) as CompletedUsers,
                    SUM(CASE WHEN p.IsActive = 1 AND p.RequiresBackup = 1 
                             AND (m.State IS NULL OR m.State NOT IN ('BackupCompleted', 'ReadyForReset'))
                             AND m.IsBlocking = 1 THEN 1 ELSE 0 END) as BlockingUsers
                FROM UserProfiles p
                LEFT JOIN MigrationStates m ON p.UserId = m.UserId";

            using var reader = await countCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                status.TotalUsers = reader.GetInt32(0);
                status.ActiveUsers = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                status.CompletedUsers = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                status.BlockingUsers = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            }

            // Get blocking user names
            using var blockingCommand = connection.CreateCommand();
            blockingCommand.CommandText = @"
                SELECT p.UserName
                FROM UserProfiles p
                LEFT JOIN MigrationStates m ON p.UserId = m.UserId
                WHERE p.IsActive = 1 AND p.RequiresBackup = 1
                AND (m.State IS NULL OR m.State NOT IN ('BackupCompleted', 'ReadyForReset'))
                AND (m.IsBlocking IS NULL OR m.IsBlocking = 1)";

            using var blockingReader = await blockingCommand.ExecuteReaderAsync(cancellationToken);
            while (await blockingReader.ReadAsync(cancellationToken))
            {
                status.BlockingUserNames.Add(blockingReader.GetString(0));
            }

            status.CanReset = status.BlockingUsers == 0;

            // Estimate ready time based on average completion rate
            if (status.BlockingUsers > 0)
            {
                // Simple estimation: assume 4 hours per remaining user
                status.EstimatedReadyTime = DateTime.UtcNow.AddHours(status.BlockingUsers * 4);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get migration readiness status");
        }

        return status;
    }

    public async Task<IEnumerable<UserMigrationSummary>> GetMigrationSummariesAsync(CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<string, UserMigrationSummary>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get user and migration info
            using var userCommand = connection.CreateCommand();
            userCommand.CommandText = @"
                SELECT p.UserId, p.UserName, 
                       COALESCE(m.State, 'NotStarted') as State, 
                       COALESCE(m.Progress, 0) as Progress,
                       COALESCE(m.IsBlocking, 1) as IsBlocking
                FROM UserProfiles p
                LEFT JOIN MigrationStates m ON p.UserId = m.UserId
                WHERE p.IsActive = 1 AND p.RequiresBackup = 1";

            using var userReader = await userCommand.ExecuteReaderAsync(cancellationToken);
            while (await userReader.ReadAsync(cancellationToken))
            {
                var summary = new UserMigrationSummary
                {
                    UserId = userReader.GetString(0),
                    UserName = userReader.GetString(1),
                    State = Enum.Parse<MigrationStateType>(userReader.GetString(2)),
                    OverallProgress = userReader.GetInt32(3),
                    IsBlocking = userReader.GetBoolean(4)
                };
                summaries[summary.UserId] = summary;
            }

            // Get backup operation details
            using var backupCommand = connection.CreateCommand();
            backupCommand.CommandText = @"
                SELECT b.UserId, b.Category, b.Status, 
                       SUM(b.BytesTransferred) / 1048576 as SizeMB
                FROM BackupOperations b
                WHERE b.UserId IN (SELECT UserId FROM UserProfiles WHERE IsActive = 1)
                GROUP BY b.UserId, b.Category, b.Status";

            using var backupReader = await backupCommand.ExecuteReaderAsync(cancellationToken);
            while (await backupReader.ReadAsync(cancellationToken))
            {
                var userId = backupReader.GetString(0);
                if (summaries.TryGetValue(userId, out var summary))
                {
                    var category = backupReader.GetString(1);
                    var status = Enum.Parse<BackupStatus>(backupReader.GetString(2));
                    summary.CategoryStatuses[category] = status;
                    summary.TotalBackupSizeMB += backupReader.IsDBNull(3) ? 0 : backupReader.GetInt64(3);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get migration summaries");
        }

        return summaries.Values;
    }

    public async Task<IEnumerable<string>> GetUsersRequiringAttentionAsync(CancellationToken cancellationToken)
    {
        var users = new List<string>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT DISTINCT UserId FROM (
                    -- Users with attention reasons
                    SELECT UserId FROM MigrationStates 
                    WHERE AttentionReason IS NOT NULL AND AttentionReason != ''
                    
                    UNION
                    
                    -- Users with sync errors
                    SELECT UserId FROM OneDriveSync
                    WHERE ErrorCount > 0 OR SyncStatus = 'Error'
                    
                    UNION
                    
                    -- Users with failed backups
                    SELECT UserId FROM BackupOperations
                    WHERE Status = 'Failed' AND CompletedAt > datetime('now', '-1 day')
                    
                    UNION
                    
                    -- Users with open escalations
                    SELECT UserId FROM ITEscalations
                    WHERE Status = 'Open' AND UserId IS NOT NULL
                )";

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                users.Add(reader.GetString(0));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get users requiring attention");
        }

        return users;
    }

    #endregion

    #region Maintenance Operations

    public async Task CleanupStaleOperationsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var cutoffTime = DateTime.UtcNow - staleThreshold;

            // Mark stale migrations
            using var migrationCommand = connection.CreateCommand();
            migrationCommand.CommandText = @"
                UPDATE MigrationStates 
                SET AttentionReason = 'Operation timed out - marked as stale'
                WHERE State IN ('Initializing', 'BackupInProgress', 'SyncInProgress')
                AND LastUpdated < @cutoffTime";

            migrationCommand.Parameters.AddWithValue("@cutoffTime", cutoffTime);
            var migrationsAffected = await migrationCommand.ExecuteNonQueryAsync(cancellationToken);

            // Mark stale backup operations
            using var backupCommand = connection.CreateCommand();
            backupCommand.CommandText = @"
                UPDATE BackupOperations 
                SET Status = 'Failed', 
                    ErrorCode = 'TIMEOUT',
                    ErrorMessage = 'Operation timed out',
                    CompletedAt = CURRENT_TIMESTAMP
                WHERE Status = 'InProgress' 
                AND StartedAt < @cutoffTime";

            backupCommand.Parameters.AddWithValue("@cutoffTime", cutoffTime);
            var backupsAffected = await backupCommand.ExecuteNonQueryAsync(cancellationToken);

            if (migrationsAffected > 0 || backupsAffected > 0)
            {
                _logger.LogWarning("Cleanup: {Migrations} migrations and {Backups} backups marked as stale",
                    migrationsAffected, backupsAffected);

                await RecordSystemEventAsync("Cleanup", "Warning",
                    $"Marked {migrationsAffected} migrations and {backupsAffected} backups as stale",
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup stale operations");
        }
    }

    public async Task ArchiveCompletedMigrationsAsync(int daysToKeep, CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

            // Archive old system events
            using var command = connection.CreateCommand();
            command.CommandText = @"
                DELETE FROM SystemEvents 
                WHERE Timestamp < @cutoffDate
                AND EventType NOT IN ('ITEscalation', 'MigrationCompleted')";

            command.Parameters.AddWithValue("@cutoffDate", cutoffDate);
            var eventsDeleted = await command.ExecuteNonQueryAsync(cancellationToken);

            if (eventsDeleted > 0)
            {
                _logger.LogInformation("Archived {Count} old system events", eventsDeleted);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to archive completed migrations");
        }
    }

    public async Task<Dictionary<string, object>> GetDatabaseStatisticsAsync(CancellationToken cancellationToken)
    {
        var stats = new Dictionary<string, object>();

        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Get table row counts
            var tables = new[] { "UserProfiles", "MigrationStates", "BackupOperations",
                                 "OneDriveSync", "ITEscalations", "SystemEvents" };

            foreach (var table in tables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT COUNT(*) FROM {table}";
                var count = await command.ExecuteScalarAsync(cancellationToken);
                stats[$"{table}_Count"] = count ?? 0;
            }

            // Get database file size
            var dbPath = Path.Combine(_configuration.DataPath, "migration.db");
            if (File.Exists(dbPath))
            {
                var fileInfo = new FileInfo(dbPath);
                stats["DatabaseSizeMB"] = fileInfo.Length / (1024.0 * 1024.0);
            }

            // Get active migration count
            using var activeCommand = connection.CreateCommand();
            activeCommand.CommandText = "SELECT COUNT(*) FROM MigrationStates WHERE Status = 'Active'";
            stats["ActiveMigrations"] = await activeCommand.ExecuteScalarAsync(cancellationToken) ?? 0;

            // Get open escalation count
            using var escalationCommand = connection.CreateCommand();
            escalationCommand.CommandText = "SELECT COUNT(*) FROM ITEscalations WHERE Status = 'Open'";
            stats["OpenEscalations"] = await escalationCommand.ExecuteScalarAsync(cancellationToken) ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get database statistics");
        }

        return stats;
    }

    #endregion

    #region Helper Methods

    private UserProfile ReadUserProfile(SqliteDataReader reader)
    {
        return new UserProfile
        {
            UserId = reader.GetString(0),
            UserName = reader.GetString(1),
            DomainName = reader.IsDBNull(2) ? null : reader.GetString(2),
            ProfilePath = reader.GetString(3),
            ProfileType = Enum.Parse<ProfileType>(reader.GetString(4)),
            LastLoginTime = reader.GetDateTime(5),
            IsActive = reader.GetBoolean(6),
            ProfileSizeBytes = reader.GetInt64(7),
            RequiresBackup = reader.GetBoolean(8),
            BackupPriority = reader.GetInt32(9),
            CreatedAt = reader.GetDateTime(10),
            UpdatedAt = reader.GetDateTime(11)
        };
    }

    private MigrationState ReadMigrationState(SqliteDataReader reader)
    {
        return new MigrationState
        {
            UserId = reader.GetString(0),
            State = Enum.Parse<MigrationStateType>(reader.GetString(1)),
            Status = reader.GetString(2),
            Progress = reader.GetInt32(3),
            StartedAt = reader.IsDBNull(4) ? null : reader.GetDateTime(4),
            LastUpdated = reader.GetDateTime(5),
            CompletedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            Deadline = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            AttentionReason = reader.IsDBNull(8) ? null : reader.GetString(8),
            DelayCount = reader.GetInt32(9),
            IsBlocking = reader.GetBoolean(10)
        };
    }

    private BackupOperation ReadBackupOperation(SqliteDataReader reader)
    {
        return new BackupOperation
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            OperationId = reader.GetString(2),
            ProviderName = reader.GetString(3),
            Category = reader.GetString(4),
            Status = Enum.Parse<BackupStatus>(reader.GetString(5)),
            StartedAt = reader.GetDateTime(6),
            CompletedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            Progress = reader.GetInt32(8),
            BytesTotal = reader.GetInt64(9),
            BytesTransferred = reader.GetInt64(10),
            ItemsTotal = reader.GetInt32(11),
            ItemsCompleted = reader.GetInt32(12),
            ErrorCode = reader.IsDBNull(13) ? null : reader.GetString(13),
            ErrorMessage = reader.IsDBNull(14) ? null : reader.GetString(14),
            RetryCount = reader.GetInt32(15),
            ManifestPath = reader.IsDBNull(16) ? null : reader.GetString(16)
        };
    }

    private OneDriveSyncStatusRecord ReadOneDriveSyncStatus(SqliteDataReader reader)
    {
        return new OneDriveSyncStatusRecord
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            IsInstalled = reader.GetBoolean(2),
            IsSignedIn = reader.GetBoolean(3),
            AccountEmail = reader.IsDBNull(4) ? null : reader.GetString(4),
            SyncFolderPath = reader.IsDBNull(5) ? null : reader.GetString(5),
            SyncStatus = reader.GetString(6),
            QuotaTotalMB = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            QuotaUsedMB = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            QuotaAvailableMB = reader.IsDBNull(9) ? null : reader.GetInt64(9),
            LastSyncTime = reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            LastSyncError = reader.IsDBNull(11) ? null : reader.GetString(11),
            ErrorCount = reader.GetInt32(12),
            CheckedAt = reader.GetDateTime(13)
        };
    }

    private ITEscalation ReadITEscalation(SqliteDataReader reader)
    {
        return new ITEscalation
        {
            Id = reader.GetInt32(0),
            UserId = reader.IsDBNull(1) ? null : reader.GetString(1),
            TriggerType = Enum.Parse<EscalationTriggerType>(reader.GetString(2)),
            TriggerReason = reader.GetString(3),
            Details = reader.IsDBNull(4) ? null : reader.GetString(4),
            TicketNumber = reader.IsDBNull(5) ? null : reader.GetString(5),
            Status = reader.GetString(6),
            CreatedAt = reader.GetDateTime(7),
            ResolvedAt = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            ResolutionNotes = reader.IsDBNull(9) ? null : reader.GetString(9),
            AutoTriggered = reader.GetBoolean(10)
        };
    }

    private DelayRequest ReadDelayRequest(SqliteDataReader reader)
    {
        return new DelayRequest
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            RequestedAt = reader.GetDateTime(2),
            RequestedDelayHours = reader.GetInt32(3),
            Reason = reader.GetString(4),
            Status = reader.GetString(5),
            ApprovedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            NewDeadline = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
        };
    }

    private UserClassificationRecord ReadUserClassificationRecord(SqliteDataReader reader)
    {
        return new UserClassificationRecord
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            Classification = Enum.Parse<ProfileClassification>(reader.GetString(2)),
            ClassificationDate = reader.GetDateTime(3),
            Confidence = reader.GetDouble(4),
            Reason = reader.GetString(5),
            RuleSetName = reader.IsDBNull(6) ? null : reader.GetString(6),
            RuleSetVersion = reader.IsDBNull(7) ? null : reader.GetString(7),
            IsOverridden = reader.GetBoolean(8),
            ActivityScore = reader.IsDBNull(9) ? null : reader.GetInt32(9),
            CreatedAt = reader.GetDateTime(10),
            UpdatedAt = reader.GetDateTime(11)
        };
    }

    private ClassificationHistoryEntry ReadClassificationHistoryEntry(SqliteDataReader reader)
    {
        return new ClassificationHistoryEntry
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            OldClassification = reader.IsDBNull(2) ? null : Enum.Parse<ProfileClassification>(reader.GetString(2)),
            NewClassification = Enum.Parse<ProfileClassification>(reader.GetString(3)),
            ChangeDate = reader.GetDateTime(4),
            Reason = reader.GetString(5),
            ActivitySnapshot = reader.IsDBNull(6) ? null : reader.GetString(6),
            CreatedAt = reader.GetDateTime(7)
        };
    }

    private ClassificationOverride ReadClassificationOverride(SqliteDataReader reader)
    {
        return new ClassificationOverride
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            OverrideClassification = Enum.Parse<ProfileClassification>(reader.GetString(2)),
            OverrideDate = reader.GetDateTime(3),
            OverrideBy = reader.GetString(4),
            Reason = reader.GetString(5),
            ExpiryDate = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
            IsActive = reader.GetBoolean(7)
        };
    }

    private ClassificationOverrideHistory ReadClassificationOverrideHistory(SqliteDataReader reader)
    {
        return new ClassificationOverrideHistory
        {
            Id = reader.GetInt32(0),
            UserId = reader.GetString(1),
            OldClassification = reader.IsDBNull(2) ? null : reader.GetString(2),
            NewClassification = reader.IsDBNull(3) ? null : reader.GetString(3),
            ChangeDate = reader.GetDateTime(4),
            ChangedBy = reader.GetString(5),
            Reason = reader.GetString(6)
        };
    }

    #endregion

    #region Sync Operation Management (Phase 3.2 Placeholder)

    public async Task<int> CreateSyncOperationAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating sync operation for user {UserId}, folder {Folder}", 
            operation.UserSid, operation.FolderPath);
        
        // Placeholder implementation - return a dummy ID
        await Task.CompletedTask;
        return 1;
    }

    public async Task UpdateSyncOperationAsync(SyncOperation operation, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating sync operation {Id}", operation.Id);
        await Task.CompletedTask;
    }

    public async Task<SyncOperation?> GetSyncOperationAsync(int operationId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting sync operation {Id}", operationId);
        await Task.CompletedTask;
        return null;
    }

    public async Task<SyncOperation?> GetActiveSyncOperationAsync(string userSid, string folderPath, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting active sync operation for user {UserId}, folder {Folder}", 
            userSid, folderPath);
        await Task.CompletedTask;
        return null;
    }

    public async Task<IEnumerable<SyncOperation>> GetSyncOperationsAsync(string userSid, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting sync operations for user {UserId}", userSid);
        await Task.CompletedTask;
        return Enumerable.Empty<SyncOperation>();
    }

    public async Task<IEnumerable<SyncOperation>> GetPendingSyncOperationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting pending sync operations");
        await Task.CompletedTask;
        return Enumerable.Empty<SyncOperation>();
    }

    public async Task IncrementSyncRetryCountAsync(int syncOperationId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Incrementing retry count for sync operation {Id}", syncOperationId);
        await Task.CompletedTask;
    }

    #endregion

    #region Sync Error Management (Phase 3.2 Placeholder)

    public async Task<int> RecordSyncErrorAsync(SyncError error, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Recording sync error for file {FilePath}: {Error}", 
            error.FilePath, error.ErrorMessage);
        await Task.CompletedTask;
        return 1;
    }

    public async Task<IEnumerable<SyncError>> GetSyncErrorsAsync(int syncOperationId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting sync errors for operation {Id}", syncOperationId);
        await Task.CompletedTask;
        return Enumerable.Empty<SyncError>();
    }

    public async Task<IEnumerable<SyncError>> GetUnresolvedSyncOperationErrorsAsync(string userSid, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting unresolved sync errors for user {UserId}", userSid);
        await Task.CompletedTask;
        return Enumerable.Empty<SyncError>();
    }

    public async Task MarkSyncOperationErrorResolvedAsync(int errorId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Marking sync error {Id} as resolved", errorId);
        await Task.CompletedTask;
    }

    public async Task EscalateSyncErrorsAsync(int syncOperationId, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Escalating sync errors for operation {Id}", syncOperationId);
        await Task.CompletedTask;
    }

    public async Task<int> GetSyncErrorCountAsync(string userSid, bool unresolvedOnly = true, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting sync error count for user {UserId}", userSid);
        await Task.CompletedTask;
        return 0;
    }

    #endregion

    public void Dispose()
    {
        // SQLite connections are disposed after each use
        // Nothing to dispose at the class level
    }
}
