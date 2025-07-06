namespace MigrationTool.Service.Core;

public interface IServiceManager
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task PerformHealthCheckAsync(CancellationToken cancellationToken);
    Task CheckMigrationStatusAsync(CancellationToken cancellationToken);
    Task CleanupAsync(CancellationToken cancellationToken);
}