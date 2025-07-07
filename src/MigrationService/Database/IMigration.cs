namespace MigrationTool.Service.Database;

public interface IMigration
{
    /// <summary>
    /// The version number of this migration
    /// </summary>
    int Version { get; }

    /// <summary>
    /// A description of what this migration does
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Apply the migration to the database
    /// </summary>
    Task UpAsync(IDatabaseConnection connection, CancellationToken cancellationToken);

    /// <summary>
    /// Rollback the migration (if supported)
    /// </summary>
    Task DownAsync(IDatabaseConnection connection, CancellationToken cancellationToken);
}

public interface IDatabaseConnection
{
    Task ExecuteAsync(string sql, CancellationToken cancellationToken);
    Task<T?> ExecuteScalarAsync<T>(string sql, CancellationToken cancellationToken);
}