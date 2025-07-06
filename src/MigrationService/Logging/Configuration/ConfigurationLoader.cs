using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using MigrationTool.Service.Logging.Core;

namespace MigrationTool.Service.Logging.Configuration;

/// <summary>
/// Loads logging configuration from various sources (files, registry, environment variables).
/// </summary>
public class ConfigurationLoader
{
    private readonly JsonSerializerOptions _jsonOptions;
    
    /// <summary>
    /// Registry key for logging configuration.
    /// </summary>
    public const string REGISTRY_KEY = @"HKEY_LOCAL_MACHINE\SOFTWARE\MigrationTool\Logging";
    
    /// <summary>
    /// Environment variable prefix for logging configuration.
    /// </summary>
    public const string ENV_PREFIX = "MIGRATION_LOG_";
    
    /// <summary>
    /// Default configuration file paths to check.
    /// </summary>
    public static readonly string[] DEFAULT_CONFIG_PATHS = 
    {
        @"C:\ProgramData\MigrationTool\Config\logging.json",
        @"C:\Program Files\MigrationTool\Config\logging.json",
        "logging.json",
        "config\\logging.json"
    };
    
    public ConfigurationLoader()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };
    }
    
    /// <summary>
    /// Loads logging configuration from the first available source.
    /// </summary>
    /// <param name="configFilePath">Optional specific config file path.</param>
    /// <returns>The loaded configuration or a default configuration.</returns>
    public LoggingConfiguration LoadConfiguration(string? configFilePath = null)
    {
        LoggingConfiguration? config = null;
        
        // Try to load from specific file if provided
        if (!string.IsNullOrEmpty(configFilePath))
        {
            config = LoadFromFile(configFilePath);
            if (config != null)
            {
                ApplyOverrides(config);
                return config;
            }
        }
        
        // Try default file locations
        foreach (var path in DEFAULT_CONFIG_PATHS)
        {
            config = LoadFromFile(path);
            if (config != null) break;
        }
        
        // If no file found, start with default configuration
        config ??= LoggingConfiguration.CreateDefault();
        
        // Apply overrides from registry and environment variables
        ApplyOverrides(config);
        
        return config;
    }
    
    /// <summary>
    /// Loads configuration from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the configuration file.</param>
    /// <returns>The loaded configuration or null if file doesn't exist or is invalid.</returns>
    public LoggingConfiguration? LoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;
            
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<LoggingConfiguration>(json, _jsonOptions);
            
            if (config != null)
            {
                Console.WriteLine($"Loaded logging configuration from: {filePath}");
            }
            
            return config;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to load logging configuration from '{filePath}': {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Saves configuration to a JSON file.
    /// </summary>
    /// <param name="config">The configuration to save.</param>
    /// <param name="filePath">Path where to save the configuration.</param>
    /// <returns>True if saved successfully; otherwise, false.</returns>
    public bool SaveToFile(LoggingConfiguration config, string filePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new JsonStringEnumConverter() }
            };
            
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(filePath, json);
            
            Console.WriteLine($"Saved logging configuration to: {filePath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save logging configuration to '{filePath}': {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Applies configuration overrides from registry and environment variables.
    /// </summary>
    /// <param name="config">The configuration to modify.</param>
    private void ApplyOverrides(LoggingConfiguration config)
    {
        ApplyRegistryOverrides(config);
        ApplyEnvironmentOverrides(config);
    }
    
    /// <summary>
    /// Applies configuration overrides from Windows Registry.
    /// </summary>
    /// <param name="config">The configuration to modify.</param>
    private void ApplyRegistryOverrides(LoggingConfiguration config)
    {
        try
        {
            // Check global minimum level override
            var minLevelValue = Registry.GetValue(REGISTRY_KEY, "MinimumLevel", null) as string;
            if (!string.IsNullOrEmpty(minLevelValue) && Enum.TryParse<LogLevel>(minLevelValue, true, out var minLevel))
            {
                config.Global.MinimumLevel = minLevel;
                Console.WriteLine($"Applied registry override: MinimumLevel = {minLevel}");
            }
            
            // Check provider enable/disable overrides
            var fileProviderEnabled = Registry.GetValue(REGISTRY_KEY, "FileProviderEnabled", null);
            if (fileProviderEnabled is int fileEnabled)
            {
                if (config.Providers.TryGetValue("FileProvider", out var fileConfig))
                {
                    fileConfig.Enabled = fileEnabled != 0;
                    Console.WriteLine($"Applied registry override: FileProvider.Enabled = {fileConfig.Enabled}");
                }
            }
            
            var eventLogEnabled = Registry.GetValue(REGISTRY_KEY, "EventLogProviderEnabled", null);
            if (eventLogEnabled is int eventEnabled)
            {
                if (config.Providers.TryGetValue("EventLogProvider", out var eventConfig))
                {
                    eventConfig.Enabled = eventEnabled != 0;
                    Console.WriteLine($"Applied registry override: EventLogProvider.Enabled = {eventConfig.Enabled}");
                }
            }
            
            // Check log directory override
            var logDirValue = Registry.GetValue(REGISTRY_KEY, "LogDirectory", null) as string;
            if (!string.IsNullOrEmpty(logDirValue) && 
                config.Providers.TryGetValue("FileProvider", out var fileProviderConfig) &&
                fileProviderConfig is FileProviderConfiguration fileProvider)
            {
                fileProvider.LogDirectory = logDirValue;
                Console.WriteLine($"Applied registry override: LogDirectory = {logDirValue}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply registry overrides: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Applies configuration overrides from environment variables.
    /// </summary>
    /// <param name="config">The configuration to modify.</param>
    private void ApplyEnvironmentOverrides(LoggingConfiguration config)
    {
        try
        {
            // Check global minimum level
            var minLevelEnv = Environment.GetEnvironmentVariable($"{ENV_PREFIX}MINIMUM_LEVEL");
            if (!string.IsNullOrEmpty(minLevelEnv) && Enum.TryParse<LogLevel>(minLevelEnv, true, out var minLevel))
            {
                config.Global.MinimumLevel = minLevel;
                Console.WriteLine($"Applied environment override: MinimumLevel = {minLevel}");
            }
            
            // Check log directory
            var logDirEnv = Environment.GetEnvironmentVariable($"{ENV_PREFIX}DIRECTORY");
            if (!string.IsNullOrEmpty(logDirEnv) && 
                config.Providers.TryGetValue("FileProvider", out var fileProviderConfig) &&
                fileProviderConfig is FileProviderConfiguration fileProvider)
            {
                fileProvider.LogDirectory = logDirEnv;
                Console.WriteLine($"Applied environment override: LogDirectory = {logDirEnv}");
            }
            
            // Check provider enable/disable
            var fileEnabledEnv = Environment.GetEnvironmentVariable($"{ENV_PREFIX}FILE_ENABLED");
            if (!string.IsNullOrEmpty(fileEnabledEnv) && bool.TryParse(fileEnabledEnv, out var fileEnabled))
            {
                if (config.Providers.TryGetValue("FileProvider", out var fileConfig))
                {
                    fileConfig.Enabled = fileEnabled;
                    Console.WriteLine($"Applied environment override: FileProvider.Enabled = {fileEnabled}");
                }
            }
            
            var eventEnabledEnv = Environment.GetEnvironmentVariable($"{ENV_PREFIX}EVENTLOG_ENABLED");
            if (!string.IsNullOrEmpty(eventEnabledEnv) && bool.TryParse(eventEnabledEnv, out var eventEnabled))
            {
                if (config.Providers.TryGetValue("EventLogProvider", out var eventConfig))
                {
                    eventConfig.Enabled = eventEnabled;
                    Console.WriteLine($"Applied environment override: EventLogProvider.Enabled = {eventEnabled}");
                }
            }
            
            // Check debug mode
            var debugEnv = Environment.GetEnvironmentVariable($"{ENV_PREFIX}DEBUG");
            if (!string.IsNullOrEmpty(debugEnv) && bool.TryParse(debugEnv, out var debugEnabled) && debugEnabled)
            {
                config.Global.MinimumLevel = LogLevel.Debug;
                config.Global.EnableConsoleLogging = true;
                Console.WriteLine("Applied environment override: Debug mode enabled");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to apply environment overrides: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Creates a sample configuration file.
    /// </summary>
    /// <param name="filePath">Path where to create the sample configuration.</param>
    /// <returns>True if created successfully; otherwise, false.</returns>
    public bool CreateSampleConfiguration(string filePath)
    {
        var sampleConfig = LoggingConfiguration.CreateDefault();
        
        // Add some comments to the configuration
        sampleConfig.CategoryOverrides.Add("MigrationTool.Service.IPC", LogLevel.Debug);
        sampleConfig.CategoryOverrides.Add("MigrationTool.Backup", LogLevel.Information);
        sampleConfig.CategoryOverrides.Add("MigrationTool.Performance", LogLevel.Warning);
        
        return SaveToFile(sampleConfig, filePath);
    }
}