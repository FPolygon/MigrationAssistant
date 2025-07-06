using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Database;
using MigrationTool.Service.Database.Migrations;
using Moq;
using Xunit;

namespace MigrationService.Tests;

[Collection("Database")]
public class DatabaseMigrationTests : IDisposable
{
    private readonly Mock<ILogger<MigrationRunner>> _loggerMock;
    private readonly string _connectionString;
    private readonly string _testDbPath;
    private readonly MigrationRunner _migrationRunner;

    public DatabaseMigrationTests()
    {
        _loggerMock = new Mock<ILogger<MigrationRunner>>();
        _testDbPath = Path.Combine(Path.GetTempPath(), $"MigrationTest_{Guid.NewGuid()}.db");
        _connectionString = $"Data Source={_testDbPath};Mode=ReadWriteCreate;";
        _migrationRunner = new MigrationRunner(_loggerMock.Object, _connectionString);
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
        {
            try
            {
                File.Delete(_testDbPath);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public async Task RunMigrationsAsync_WithEmptyDatabase_ShouldCreateAllTables()
    {
        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var tables = await GetTableNamesAsync(connection);
        
        tables.Should().Contain("MigrationHistory");
        tables.Should().Contain("UserProfiles");
        tables.Should().Contain("MigrationStates");
        tables.Should().Contain("BackupOperations");
        tables.Should().Contain("OneDriveSync");
        tables.Should().Contain("ITEscalations");
        tables.Should().Contain("StateHistory");
        tables.Should().Contain("DelayRequests");
        tables.Should().Contain("ProviderResults");
        tables.Should().Contain("SystemEvents");
    }

    [Fact]
    public async Task RunMigrationsAsync_ShouldRecordMigrationInHistory()
    {
        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Version, Description FROM MigrationHistory ORDER BY Version";
        
        using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        
        reader.GetInt32(0).Should().Be(1);
        reader.GetString(1).Should().Contain("Initial database schema");
    }

    [Fact]
    public async Task RunMigrationsAsync_WhenRunTwice_ShouldNotReapplyMigrations()
    {
        // Arrange
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MigrationHistory";
        
        var count = await command.ExecuteScalarAsync();
        Convert.ToInt32(count).Should().Be(1); // Only one migration should be recorded
    }

    [Fact]
    public async Task Migration001_ShouldCreateProperIndexes()
    {
        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var indexes = await GetIndexNamesAsync(connection);
        
        indexes.Should().Contain("idx_migration_states_status");
        indexes.Should().Contain("idx_migration_states_state");
        indexes.Should().Contain("idx_backup_operations_user");
        indexes.Should().Contain("idx_backup_operations_status");
        indexes.Should().Contain("idx_onedrive_sync_user");
        indexes.Should().Contain("idx_escalations_status");
        indexes.Should().Contain("idx_escalations_user");
        indexes.Should().Contain("idx_state_history_user");
        indexes.Should().Contain("idx_state_history_timestamp");
        indexes.Should().Contain("idx_system_events_timestamp");
        indexes.Should().Contain("idx_delay_requests_user");
        indexes.Should().Contain("idx_delay_requests_status");
    }

    [Fact]
    public async Task Migration001_UserProfilesTable_ShouldHaveCorrectSchema()
    {
        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var columns = await GetTableColumnsAsync(connection, "UserProfiles");
        
        columns.Should().ContainKeys(new[] 
        {
            "UserId", "UserName", "DomainName", "ProfilePath", "ProfileType",
            "LastLoginTime", "IsActive", "ProfileSizeBytes", "RequiresBackup",
            "BackupPriority", "CreatedAt", "UpdatedAt"
        });
    }

    [Fact]
    public async Task Migration001_MigrationStatesTable_ShouldHaveForeignKey()
    {
        // Act
        await _migrationRunner.RunMigrationsAsync(CancellationToken.None);

        // Assert
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var foreignKeys = await GetForeignKeysAsync(connection, "MigrationStates");
        
        foreignKeys.Should().Contain(fk => 
            fk.Table == "MigrationStates" && 
            fk.From == "UserId" && 
            fk.ToTable == "UserProfiles" && 
            fk.To == "UserId");
    }

    [Fact]
    public async Task MigrationRunner_WithFailingMigration_ShouldRollback()
    {
        // Arrange
        var failingConnectionString = $"Data Source={_testDbPath}_failing;Mode=ReadWriteCreate;";
        var failingRunner = new FailingMigrationRunner(
            _loggerMock.Object, failingConnectionString);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () => 
            await failingRunner.RunMigrationsAsync(CancellationToken.None));

        // Verify no partial migration was applied
        using var connection = new SqliteConnection(failingConnectionString);
        await connection.OpenAsync();

        var tables = await GetTableNamesAsync(connection);
        tables.Should().NotContain("UserProfiles"); // Table should not exist due to rollback
    }

    private async Task<List<string>> GetTableNamesAsync(SqliteConnection connection)
    {
        var tables = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tables.Add(reader.GetString(0));
        }
        
        return tables;
    }

    private async Task<List<string>> GetIndexNamesAsync(SqliteConnection connection)
    {
        var indexes = new List<string>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%'";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add(reader.GetString(0));
        }
        
        return indexes;
    }

    private async Task<Dictionary<string, string>> GetTableColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new Dictionary<string, string>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({tableName})";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var name = reader.GetString(1);
            var type = reader.GetString(2);
            columns[name] = type;
        }
        
        return columns;
    }

    private async Task<List<ForeignKeyInfo>> GetForeignKeysAsync(SqliteConnection connection, string tableName)
    {
        var foreignKeys = new List<ForeignKeyInfo>();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA foreign_key_list({tableName})";
        
        using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            foreignKeys.Add(new ForeignKeyInfo
            {
                Table = tableName,
                From = reader.GetString(3),
                ToTable = reader.GetString(2),
                To = reader.GetString(4)
            });
        }
        
        return foreignKeys;
    }

    private class ForeignKeyInfo
    {
        public string Table { get; set; } = "";
        public string From { get; set; } = "";
        public string ToTable { get; set; } = "";
        public string To { get; set; } = "";
    }

    // Test helper classes
    private class FailingMigrationRunner : MigrationRunner
    {
        public FailingMigrationRunner(ILogger<MigrationRunner> logger, string connectionString)
            : base(logger, connectionString)
        {
        }

        protected override List<IMigration> LoadMigrations()
        {
            return new List<IMigration> { new FailingMigration() };
        }
    }

    private class FailingMigration : IMigration
    {
        public int Version => 999;
        public string Description => "Failing test migration";

        public async Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
        {
            // Create partial state
            await connection.ExecuteAsync(
                "CREATE TABLE UserProfiles (UserId TEXT PRIMARY KEY)", cancellationToken);
            
            // Then fail
            throw new Exception("Simulated migration failure");
        }

        public Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

public class Migration001Tests
{
    [Fact]
    public void Migration001_ShouldHaveCorrectVersionAndDescription()
    {
        // Arrange
        var migration = new Migration001_InitialSchema();

        // Assert
        migration.Version.Should().Be(1);
        migration.Description.Should().Contain("Initial database schema");
    }

    [Fact]
    public async Task Migration001_DownAsync_ShouldDropAllTables()
    {
        // Arrange
        var migration = new Migration001_InitialSchema();
        var mockConnection = new Mock<IDatabaseConnection>();
        var executedCommands = new List<string>();
        
        mockConnection.Setup(x => x.ExecuteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((sql, ct) => executedCommands.Add(sql))
            .Returns(Task.CompletedTask);

        // Act
        await migration.DownAsync(mockConnection.Object, CancellationToken.None);

        // Assert
        executedCommands.Should().ContainSingle();
        var dropCommand = executedCommands[0];
        dropCommand.Should().Contain("DROP TABLE IF EXISTS ProviderResults");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS DelayRequests");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS StateHistory");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS ITEscalations");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS OneDriveSync");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS BackupOperations");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS MigrationStates");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS SystemEvents");
        dropCommand.Should().Contain("DROP TABLE IF EXISTS UserProfiles");
    }
}