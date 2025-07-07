using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.Core;

public class ServiceManager : IServiceManager
{
    private readonly ILogger<ServiceManager> _logger;
    private readonly IStateManager _stateManager;
    private readonly ServiceConfiguration _configuration;
    private bool _isInitialized;

    public ServiceManager(
        ILogger<ServiceManager> logger,
        IStateManager stateManager,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _stateManager = stateManager;
        _configuration = configuration.Value;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            _logger.LogWarning("ServiceManager is already initialized");
            return;
        }

        _logger.LogInformation("Initializing ServiceManager");

        try
        {
            // Initialize any service-level resources
            await InitializeResourcesAsync(cancellationToken);

            // Set up scheduled tasks if needed
            await ConfigureScheduledTasksAsync(cancellationToken);

            _isInitialized = true;
            _logger.LogInformation("ServiceManager initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize ServiceManager");
            throw;
        }
    }

    public async Task PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Check database connectivity
            var dbHealth = await _stateManager.CheckHealthAsync(cancellationToken);
            if (!dbHealth)
            {
                _logger.LogWarning("Database health check failed");
            }

            // Check disk space
            CheckDiskSpace();

            // Check for stale operations
            await CheckStaleOperationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
        }
    }

    public async Task CheckMigrationStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Checking migration status");

            // Get all active migrations
            var activeMigrations = await _stateManager.GetActiveMigrationsAsync(cancellationToken);

            foreach (var migration in activeMigrations)
            {
                _logger.LogDebug("Processing migration for user: {UserId}", migration.UserId);

                // Check if migration needs attention
                if (migration.RequiresAttention())
                {
                    await HandleMigrationAttentionAsync(migration, cancellationToken);
                }

                // Update migration progress if needed
                if (migration.NeedsProgressUpdate())
                {
                    await UpdateMigrationProgressAsync(migration, cancellationToken);
                }
            }

            // Check if all users are ready for reset
            await CheckResetReadinessAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check migration status");
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing service cleanup");

        try
        {
            // Save any pending state
            await _stateManager.FlushAsync(cancellationToken);

            // Clean up temporary files
            CleanupTemporaryFiles();

            _logger.LogInformation("Service cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service cleanup");
        }
    }

    private async Task InitializeResourcesAsync(CancellationToken cancellationToken)
    {
        // Initialize any resources needed by the service
        _logger.LogDebug("Initializing service resources");

        // Ensure registry keys exist
        EnsureRegistryKeys();

        // Set up performance counters if needed
        await SetupPerformanceCountersAsync(cancellationToken);
    }

    private async Task ConfigureScheduledTasksAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Configuring scheduled tasks");

        // This is where we'd set up any Windows scheduled tasks
        // For now, the service handles its own scheduling
        await Task.CompletedTask;
    }

    private void CheckDiskSpace()
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_configuration.DataPath) ?? "C:\\");
            var freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);

            if (freeSpaceGB < 1.0)
            {
                _logger.LogWarning("Low disk space: {FreeSpaceGB:F2} GB available", freeSpaceGB);
            }
            else
            {
                _logger.LogDebug("Disk space: {FreeSpaceGB:F2} GB available", freeSpaceGB);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check disk space");
        }
    }

    private async Task CheckStaleOperationsAsync(CancellationToken cancellationToken)
    {
        // Check for operations that have been running too long
        var staleThreshold = TimeSpan.FromHours(24);
        await _stateManager.CleanupStaleOperationsAsync(staleThreshold, cancellationToken);
    }

    private async Task HandleMigrationAttentionAsync(MigrationState migration, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Migration requires attention for user: {UserId}, Reason: {Reason}",
            migration.UserId, migration.AttentionReason);

        // TODO: Implement IT escalation logic here
        await Task.CompletedTask;
    }

    private async Task UpdateMigrationProgressAsync(MigrationState migration, CancellationToken cancellationToken)
    {
        // TODO: Query actual progress from backup providers
        _logger.LogDebug("Updating progress for migration: {UserId}", migration.UserId);
        await Task.CompletedTask;
    }

    private async Task CheckResetReadinessAsync(CancellationToken cancellationToken)
    {
        var isReady = await _stateManager.AreAllUsersReadyForResetAsync(cancellationToken);

        if (isReady)
        {
            _logger.LogInformation("All users are ready for system reset");
            // TODO: Update registry to signal readiness
        }
    }

    private void CleanupTemporaryFiles()
    {
        try
        {
            var tempPath = Path.Combine(_configuration.DataPath, "Temp");
            if (Directory.Exists(tempPath))
            {
                var files = Directory.GetFiles(tempPath, "*", SearchOption.AllDirectories)
                    .Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-7));

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary file: {File}", file);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup temporary files");
        }
    }

    private void EnsureRegistryKeys()
    {
        // TODO: Create necessary registry keys for service operation
        _logger.LogDebug("Ensuring registry keys exist");
    }

    private async Task SetupPerformanceCountersAsync(CancellationToken cancellationToken)
    {
        // TODO: Set up Windows performance counters if needed
        _logger.LogDebug("Setting up performance counters");
        await Task.CompletedTask;
    }
}

