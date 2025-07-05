using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MigrationTool.Service.Core;

public class MigrationWindowsService : BackgroundService
{
    private readonly ILogger<MigrationWindowsService> _logger;
    private readonly ServiceManager _serviceManager;
    private readonly IStateManager _stateManager;
    private readonly IIpcServer _ipcServer;
    private readonly ServiceConfiguration _configuration;
    private readonly IHostApplicationLifetime _lifetime;

    public MigrationWindowsService(
        ILogger<MigrationWindowsService> logger,
        ServiceManager serviceManager,
        IStateManager stateManager,
        IIpcServer ipcServer,
        IOptions<ServiceConfiguration> configuration,
        IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _serviceManager = serviceManager;
        _stateManager = stateManager;
        _ipcServer = ipcServer;
        _configuration = configuration.Value;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Migration Service is starting");

            // Initialize service components
            await InitializeServiceAsync(stoppingToken);

            // Start the IPC server
            await _ipcServer.StartAsync(stoppingToken);

            // Main service loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Perform periodic tasks
                    await _serviceManager.PerformHealthCheckAsync(stoppingToken);
                    await _serviceManager.CheckMigrationStatusAsync(stoppingToken);

                    // Wait for the configured interval
                    await Task.Delay(TimeSpan.FromSeconds(_configuration.StateCheckIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in service main loop");
                    // Continue running unless we're stopping
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in Migration Service");
            _lifetime.StopApplication();
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migration Service OnStart called");
        
        // Ensure directories exist
        EnsureDirectoriesExist();
        
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Migration Service OnStop called");

        try
        {
            // Stop the IPC server
            await _ipcServer.StopAsync(cancellationToken);

            // Cleanup resources
            await _serviceManager.CleanupAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during service shutdown");
        }

        await base.StopAsync(cancellationToken);
        
        _logger.LogInformation("Migration Service stopped");
    }

    private async Task InitializeServiceAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initializing Migration Service components");

        try
        {
            // Initialize database
            await _stateManager.InitializeAsync(cancellationToken);
            _logger.LogInformation("State manager initialized");

            // Initialize service manager
            await _serviceManager.InitializeAsync(cancellationToken);
            _logger.LogInformation("Service manager initialized");

            // Log system information
            LogSystemInformation();

            _logger.LogInformation("Migration Service initialization completed");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize Migration Service");
            throw;
        }
    }

    private void EnsureDirectoriesExist()
    {
        try
        {
            // Create data directory
            if (!Directory.Exists(_configuration.DataPath))
            {
                Directory.CreateDirectory(_configuration.DataPath);
                _logger.LogInformation("Created data directory: {Path}", _configuration.DataPath);
            }

            // Create log directory
            if (!Directory.Exists(_configuration.LogPath))
            {
                Directory.CreateDirectory(_configuration.LogPath);
                _logger.LogInformation("Created log directory: {Path}", _configuration.LogPath);
            }

            // Create backup staging directory
            var backupPath = Path.Combine(_configuration.DataPath, "Backups");
            if (!Directory.Exists(backupPath))
            {
                Directory.CreateDirectory(backupPath);
                _logger.LogInformation("Created backup directory: {Path}", backupPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create required directories");
            throw;
        }
    }

    private void LogSystemInformation()
    {
        try
        {
            _logger.LogInformation("System Information:");
            _logger.LogInformation("  Machine Name: {MachineName}", Environment.MachineName);
            _logger.LogInformation("  OS Version: {OSVersion}", Environment.OSVersion);
            _logger.LogInformation("  64-bit OS: {Is64BitOS}", Environment.Is64BitOperatingSystem);
            _logger.LogInformation("  Processor Count: {ProcessorCount}", Environment.ProcessorCount);
            _logger.LogInformation("  Service Account: {UserName}", Environment.UserName);
            _logger.LogInformation("  .NET Version: {RuntimeVersion}", Environment.Version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log some system information");
        }
    }
}