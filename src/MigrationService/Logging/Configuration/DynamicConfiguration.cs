using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Configuration;

/// <summary>
/// Manages dynamic configuration changes for the logging system.
/// Supports file watching, runtime updates, and configuration validation.
/// </summary>
public class DynamicConfiguration : IDisposable
{
    private readonly ConfigurationLoader _loader;
    private readonly LoggingService _loggingService;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private LoggingConfiguration _currentConfiguration;
    private bool _disposed;

    /// <summary>
    /// Event raised when configuration changes.
    /// </summary>
    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    /// <summary>
    /// Gets the current logging configuration.
    /// </summary>
    public LoggingConfiguration CurrentConfiguration => _currentConfiguration;

    /// <summary>
    /// Initializes a new instance of the DynamicConfiguration.
    /// </summary>
    /// <param name="loggingService">The logging service to update when configuration changes.</param>
    /// <param name="loader">The configuration loader.</param>
    public DynamicConfiguration(LoggingService loggingService, ConfigurationLoader? loader = null)
    {
        _loggingService = loggingService ?? throw new ArgumentNullException(nameof(loggingService));
        _loader = loader ?? new ConfigurationLoader();
        _currentConfiguration = LoggingConfiguration.CreateDefault();
    }

    /// <summary>
    /// Initializes the dynamic configuration system.
    /// </summary>
    /// <param name="configFilePath">Optional specific configuration file to load and watch.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task InitializeAsync(string? configFilePath = null)
    {
        // Load initial configuration
        _currentConfiguration = _loader.LoadConfiguration(configFilePath);

        // Apply configuration to logging service
        await ApplyConfigurationAsync(_currentConfiguration);

        // Set up file watching if a specific file was provided
        if (!string.IsNullOrEmpty(configFilePath) && File.Exists(configFilePath))
        {
            WatchConfigurationFile(configFilePath);
        }
        else
        {
            // Watch default configuration file locations
            foreach (var path in ConfigurationLoader.DEFAULT_CONFIG_PATHS)
            {
                if (File.Exists(path))
                {
                    WatchConfigurationFile(path);
                    break; // Only watch the first existing file
                }
            }
        }
    }

    /// <summary>
    /// Updates the log level for a specific category at runtime.
    /// </summary>
    /// <param name="category">The category to update.</param>
    /// <param name="level">The new log level.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateCategoryLevelAsync(string category, LogLevel level)
    {
        await _updateLock.WaitAsync();
        try
        {
            _currentConfiguration.CategoryOverrides[category] = level;
            await ApplyConfigurationAsync(_currentConfiguration);

            OnConfigurationChanged(new ConfigurationChangedEventArgs
            {
                ChangeType = ConfigurationChangeType.CategoryLevelChanged,
                Category = category,
                NewLevel = level
            });
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Updates the global minimum log level at runtime.
    /// </summary>
    /// <param name="level">The new minimum log level.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateGlobalLevelAsync(LogLevel level)
    {
        await _updateLock.WaitAsync();
        try
        {
            _currentConfiguration.Global.MinimumLevel = level;
            await ApplyConfigurationAsync(_currentConfiguration);

            OnConfigurationChanged(new ConfigurationChangedEventArgs
            {
                ChangeType = ConfigurationChangeType.GlobalLevelChanged,
                NewLevel = level
            });
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Enables or disables a specific logging provider at runtime.
    /// </summary>
    /// <param name="providerName">The name of the provider.</param>
    /// <param name="enabled">Whether to enable or disable the provider.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task UpdateProviderStateAsync(string providerName, bool enabled)
    {
        await _updateLock.WaitAsync();
        try
        {
            if (_currentConfiguration.Providers.TryGetValue(providerName, out var providerConfig))
            {
                providerConfig.Enabled = enabled;
                await ApplyConfigurationAsync(_currentConfiguration);

                OnConfigurationChanged(new ConfigurationChangedEventArgs
                {
                    ChangeType = ConfigurationChangeType.ProviderStateChanged,
                    ProviderName = providerName,
                    ProviderEnabled = enabled
                });
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Reloads configuration from file.
    /// </summary>
    /// <param name="configFilePath">Optional specific configuration file to reload.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ReloadConfigurationAsync(string? configFilePath = null)
    {
        await _updateLock.WaitAsync();
        try
        {
            var newConfiguration = _loader.LoadConfiguration(configFilePath);

            if (ValidateConfiguration(newConfiguration))
            {
                _currentConfiguration = newConfiguration;
                await ApplyConfigurationAsync(_currentConfiguration);

                OnConfigurationChanged(new ConfigurationChangedEventArgs
                {
                    ChangeType = ConfigurationChangeType.FullReload,
                    ConfigurationFilePath = configFilePath
                });
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    /// <summary>
    /// Gets the effective log level for a specific category.
    /// </summary>
    /// <param name="category">The category to check.</param>
    /// <returns>The effective log level for the category.</returns>
    public LogLevel GetEffectiveLevel(string category)
    {
        // Check category-specific overrides
        if (_currentConfiguration.CategoryOverrides.TryGetValue(category, out var specificLevel))
        {
            return specificLevel;
        }

        // Check partial matches
        foreach (var (prefix, level) in _currentConfiguration.CategoryOverrides)
        {
            if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return level;
            }
        }

        return _currentConfiguration.Global.MinimumLevel;
    }

    private void WatchConfigurationFile(string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath);

            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
                return;

            var watcher = new FileSystemWatcher(directory, fileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };

            watcher.Changed += async (sender, e) =>
            {
                // Debounce multiple rapid file change events
                await Task.Delay(500);
                await ReloadConfigurationAsync(e.FullPath);
            };

            _watchers[filePath] = watcher;
            Console.WriteLine($"Watching configuration file: {filePath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to setup file watcher for '{filePath}': {ex.Message}");
        }
    }

    private async Task ApplyConfigurationAsync(LoggingConfiguration configuration)
    {
        // Convert configuration to logging settings for each provider
        foreach (var (providerName, providerConfig) in configuration.Providers)
        {
            var settings = providerConfig.ToLoggingSettings(
                configuration.Global,
                configuration.CategoryOverrides);

            // Apply settings to the logging service
            await _loggingService.ConfigureAsync(settings);
        }
    }

    private bool ValidateConfiguration(LoggingConfiguration configuration)
    {
        try
        {
            if (configuration == null)
            {
                Console.Error.WriteLine("Configuration is null");
                return false;
            }

            if (configuration.Global == null)
            {
                Console.Error.WriteLine("Global configuration is null");
                return false;
            }

            // Validate that at least one provider is enabled
            var hasEnabledProvider = false;
            foreach (var provider in configuration.Providers.Values)
            {
                if (provider.Enabled)
                {
                    hasEnabledProvider = true;
                    break;
                }
            }

            if (!hasEnabledProvider)
            {
                Console.Error.WriteLine("No logging providers are enabled");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Configuration validation failed: {ex.Message}");
            return false;
        }
    }

    private void OnConfigurationChanged(ConfigurationChangedEventArgs args)
    {
        try
        {
            ConfigurationChanged?.Invoke(this, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error in configuration change handler: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        foreach (var watcher in _watchers.Values)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }

        _watchers.Clear();
        _updateLock.Dispose();
    }
}

/// <summary>
/// Event arguments for configuration changes.
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// The type of configuration change.
    /// </summary>
    public ConfigurationChangeType ChangeType { get; set; }

    /// <summary>
    /// The category that changed (for category-specific changes).
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The new log level (for level changes).
    /// </summary>
    public LogLevel? NewLevel { get; set; }

    /// <summary>
    /// The provider name (for provider-specific changes).
    /// </summary>
    public string? ProviderName { get; set; }

    /// <summary>
    /// The new provider state (for provider state changes).
    /// </summary>
    public bool? ProviderEnabled { get; set; }

    /// <summary>
    /// The configuration file path (for file reloads).
    /// </summary>
    public string? ConfigurationFilePath { get; set; }
}

/// <summary>
/// Types of configuration changes.
/// </summary>
public enum ConfigurationChangeType
{
    /// <summary>
    /// A category-specific log level was changed.
    /// </summary>
    CategoryLevelChanged,

    /// <summary>
    /// The global minimum log level was changed.
    /// </summary>
    GlobalLevelChanged,

    /// <summary>
    /// A provider was enabled or disabled.
    /// </summary>
    ProviderStateChanged,

    /// <summary>
    /// The entire configuration was reloaded.
    /// </summary>
    FullReload
}