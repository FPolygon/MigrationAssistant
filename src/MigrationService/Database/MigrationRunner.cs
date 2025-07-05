using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace MigrationTool.Service.Database;

public class MigrationRunner
{
    private readonly ILogger<MigrationRunner> _logger;
    private readonly string _connectionString;
    private readonly List<IMigration> _migrations;

    public MigrationRunner(ILogger<MigrationRunner> logger, string connectionString)
    {
        _logger = logger;
        _connectionString = connectionString;
        _migrations = LoadMigrations();
    }

    public async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting database migration check");

        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Create migration history table if it doesn't exist
        await CreateMigrationHistoryTableAsync(connection, cancellationToken);

        // Get current version
        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);
        _logger.LogInformation("Current database version: {Version}", currentVersion);

        // Get migrations to run
        var pendingMigrations = _migrations
            .Where(m => m.Version > currentVersion)
            .OrderBy(m => m.Version)
            .ToList();

        if (!pendingMigrations.Any())
        {
            _logger.LogInformation("Database is up to date");
            return;
        }

        _logger.LogInformation("Found {Count} pending migrations", pendingMigrations.Count);

        // Run migrations
        foreach (var migration in pendingMigrations)
        {
            _logger.LogInformation("Applying migration {Version}: {Description}", 
                migration.Version, migration.Description);

            using var transaction = connection.BeginTransaction();
            try
            {
                var dbConnection = new SqliteDatabaseConnection(connection, transaction);
                await migration.UpAsync(dbConnection, cancellationToken);

                // Record migration
                await RecordMigrationAsync(connection, transaction, migration, cancellationToken);

                transaction.Commit();
                _logger.LogInformation("Migration {Version} applied successfully", migration.Version);
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed to apply migration {Version}", migration.Version);
                throw new Exception($"Migration {migration.Version} failed: {ex.Message}", ex);
            }
        }

        _logger.LogInformation("All migrations completed successfully");
    }

    private async Task CreateMigrationHistoryTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
            CREATE TABLE IF NOT EXISTS MigrationHistory (
                Version INTEGER PRIMARY KEY,
                Description TEXT NOT NULL,
                AppliedAt DATETIME NOT NULL,
                AppliedBy TEXT NOT NULL
            )";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        const string sql = "SELECT MAX(Version) FROM MigrationHistory";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result == DBNull.Value || result == null ? 0 : Convert.ToInt32(result);
    }

    private async Task RecordMigrationAsync(SqliteConnection connection, SqliteTransaction transaction, 
        IMigration migration, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO MigrationHistory (Version, Description, AppliedAt, AppliedBy)
            VALUES (@version, @description, @appliedAt, @appliedBy)";

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddWithValue("@version", migration.Version);
        command.Parameters.AddWithValue("@description", migration.Description);
        command.Parameters.AddWithValue("@appliedAt", DateTime.UtcNow);
        command.Parameters.AddWithValue("@appliedBy", Environment.UserName);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    protected virtual List<IMigration> LoadMigrations()
    {
        var migrations = new List<IMigration>();
        var migrationTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IMigration).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

        foreach (var type in migrationTypes)
        {
            if (Activator.CreateInstance(type) is IMigration migration)
            {
                migrations.Add(migration);
            }
        }

        return migrations.OrderBy(m => m.Version).ToList();
    }

    private class SqliteDatabaseConnection : IDatabaseConnection
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteTransaction _transaction;

        public SqliteDatabaseConnection(SqliteConnection connection, SqliteTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public async Task ExecuteAsync(string sql, CancellationToken cancellationToken)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken)
        {
            using var command = _connection.CreateCommand();
            command.Transaction = _transaction;
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            
            if (result == null || result == DBNull.Value)
                return default;
                
            return (T)Convert.ChangeType(result, typeof(T));
        }
    }
}