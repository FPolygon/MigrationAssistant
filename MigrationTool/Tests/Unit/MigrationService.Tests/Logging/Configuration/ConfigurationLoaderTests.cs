using System;
using System.IO;
using System.Text.Json;
using FluentAssertions;
using MigrationTool.Service.Logging.Configuration;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Rotation;
using Xunit;

namespace MigrationService.Tests.Logging.Configuration;

public class ConfigurationLoaderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly ConfigurationLoader _loader;
    
    public ConfigurationLoaderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MigrationLogConfigTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        _loader = new ConfigurationLoader();
    }
    
    [Fact]
    public void LoadConfiguration_WithNonExistentFile_ShouldReturnDefaultConfiguration()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.json");
        
        // Act
        var config = _loader.LoadConfiguration(nonExistentPath);
        
        // Assert
        config.Should().NotBeNull();
        config.Global.Should().NotBeNull();
        config.Providers.Should().NotBeEmpty();
    }
    
    [Fact]
    public void LoadFromFile_WithValidConfiguration_ShouldLoadCorrectly()
    {
        // Arrange
        var configPath = Path.Combine(_testDirectory, "valid-config.json");
        var testConfig = new LoggingConfiguration
        {
            Global = new GlobalLoggingSettings
            {
                MinimumLevel = LogLevel.Debug,
                EnableContextFlow = false,
                MaxBufferSize = 5000
            },
            Providers =
            {
                ["FileProvider"] = new FileProviderConfiguration
                {
                    Enabled = true,
                    LogDirectory = @"C:\TestLogs",
                    FilePrefix = "test-app",
                    MaxFileSizeBytes = 5 * 1024 * 1024,
                    RotationInterval = RotationInterval.Hourly,
                    RetentionDays = 7
                }
            },
            CategoryOverrides =
            {
                ["TestCategory"] = LogLevel.Warning
            }
        };
        
        var json = JsonSerializer.Serialize(testConfig, new JsonSerializerOptions 
        { 
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });
        File.WriteAllText(configPath, json);
        
        // Act
        var loaded = _loader.LoadFromFile(configPath);
        
        // Assert
        loaded.Should().NotBeNull();
        loaded!.Global.MinimumLevel.Should().Be(LogLevel.Debug);
        loaded.Global.EnableContextFlow.Should().BeFalse();
        loaded.Global.MaxBufferSize.Should().Be(5000);
        
        loaded.Providers.Should().ContainKey("FileProvider");
        var fileProvider = loaded.Providers["FileProvider"] as FileProviderConfiguration;
        fileProvider.Should().NotBeNull();
        fileProvider!.LogDirectory.Should().Be(@"C:\TestLogs");
        fileProvider.FilePrefix.Should().Be("test-app");
        fileProvider.MaxFileSizeBytes.Should().Be(5 * 1024 * 1024);
        fileProvider.RotationInterval.Should().Be(RotationInterval.Hourly);
        fileProvider.RetentionDays.Should().Be(7);
        
        loaded.CategoryOverrides.Should().ContainKey("TestCategory");
        loaded.CategoryOverrides["TestCategory"].Should().Be(LogLevel.Warning);
    }
    
    [Fact]
    public void LoadFromFile_WithInvalidJson_ShouldReturnNull()
    {
        // Arrange
        var configPath = Path.Combine(_testDirectory, "invalid-config.json");
        File.WriteAllText(configPath, "{ invalid json }");
        
        // Act
        var loaded = _loader.LoadFromFile(configPath);
        
        // Assert
        loaded.Should().BeNull();
    }
    
    [Fact]
    public void LoadFromFile_WithNonExistentFile_ShouldReturnNull()
    {
        // Arrange
        var configPath = Path.Combine(_testDirectory, "nonexistent.json");
        
        // Act
        var loaded = _loader.LoadFromFile(configPath);
        
        // Assert
        loaded.Should().BeNull();
    }
    
    [Fact]
    public void SaveToFile_ShouldCreateValidJsonFile()
    {
        // Arrange
        var configPath = Path.Combine(_testDirectory, "saved-config.json");
        var config = LoggingConfiguration.CreateDefault();
        config.Global.MinimumLevel = LogLevel.Warning;
        
        // Act
        var saved = _loader.SaveToFile(config, configPath);
        
        // Assert
        saved.Should().BeTrue();
        File.Exists(configPath).Should().BeTrue();
        
        var json = File.ReadAllText(configPath);
        json.Should().Contain("\"minimumLevel\":");
        json.Should().Contain("\"warning\"");
        
        // Verify round-trip
        var reloaded = _loader.LoadFromFile(configPath);
        reloaded.Should().NotBeNull();
        reloaded!.Global.MinimumLevel.Should().Be(LogLevel.Warning);
    }
    
    [Fact]
    public void SaveToFile_WithInvalidPath_ShouldReturnFalse()
    {
        // Arrange
        var invalidPath = "Z:\\NonExistentDrive\\config.json";
        var config = LoggingConfiguration.CreateDefault();
        
        // Act
        var saved = _loader.SaveToFile(config, invalidPath);
        
        // Assert
        saved.Should().BeFalse();
    }
    
    [Fact]
    public void CreateSampleConfiguration_ShouldCreateValidFile()
    {
        // Arrange
        var samplePath = Path.Combine(_testDirectory, "sample-config.json");
        
        // Act
        var created = _loader.CreateSampleConfiguration(samplePath);
        
        // Assert
        created.Should().BeTrue();
        File.Exists(samplePath).Should().BeTrue();
        
        var loaded = _loader.LoadFromFile(samplePath);
        loaded.Should().NotBeNull();
        loaded!.Global.Should().NotBeNull();
        loaded.Providers.Should().NotBeEmpty();
        loaded.CategoryOverrides.Should().NotBeEmpty();
    }
    
    [Fact]
    public void LoadConfiguration_WithEnvironmentOverrides_ShouldApplyOverrides()
    {
        // Arrange
        var originalMinLevel = Environment.GetEnvironmentVariable("MIGRATION_LOG_MINIMUM_LEVEL");
        var originalDirectory = Environment.GetEnvironmentVariable("MIGRATION_LOG_DIRECTORY");
        var originalDebug = Environment.GetEnvironmentVariable("MIGRATION_LOG_DEBUG");
        
        try
        {
            Environment.SetEnvironmentVariable("MIGRATION_LOG_MINIMUM_LEVEL", "Error");
            Environment.SetEnvironmentVariable("MIGRATION_LOG_DIRECTORY", @"C:\EnvOverrideLogs");
            Environment.SetEnvironmentVariable("MIGRATION_LOG_DEBUG", "true");
            
            // Act
            var config = _loader.LoadConfiguration();
            
            // Assert
            config.Global.MinimumLevel.Should().Be(LogLevel.Debug); // Debug mode overrides minimum level
            config.Global.EnableConsoleLogging.Should().BeTrue();
            
            if (config.Providers.TryGetValue("FileProvider", out var fileProvider) &&
                fileProvider is FileProviderConfiguration fileConfig)
            {
                fileConfig.LogDirectory.Should().Be(@"C:\EnvOverrideLogs");
            }
        }
        finally
        {
            // Cleanup environment variables
            Environment.SetEnvironmentVariable("MIGRATION_LOG_MINIMUM_LEVEL", originalMinLevel);
            Environment.SetEnvironmentVariable("MIGRATION_LOG_DIRECTORY", originalDirectory);
            Environment.SetEnvironmentVariable("MIGRATION_LOG_DEBUG", originalDebug);
        }
    }
    
    [Fact]
    public void FileProviderConfiguration_ToLoggingSettings_ShouldConvertCorrectly()
    {
        // Arrange
        var fileConfig = new FileProviderConfiguration
        {
            Enabled = true,
            MinimumLevel = LogLevel.Warning,
            LogDirectory = @"C:\TestLogs",
            FilePrefix = "test",
            MaxFileSizeBytes = 1024 * 1024,
            RotationInterval = RotationInterval.Daily,
            RetentionDays = 14,
            UseJsonFormat = true,
            UseUtc = false
        };
        
        var globalSettings = new GlobalLoggingSettings { MinimumLevel = LogLevel.Information };
        var categoryOverrides = new System.Collections.Generic.Dictionary<string, LogLevel>();
        
        // Act
        var settings = fileConfig.ToLoggingSettings(globalSettings, categoryOverrides);
        
        // Assert
        settings.Should().NotBeNull();
        settings.Enabled.Should().BeTrue();
        settings.MinimumLevel.Should().Be(LogLevel.Warning); // Provider override
        
        settings.ProviderSettings.Should().ContainKey("LogDirectory");
        settings.ProviderSettings["LogDirectory"].Should().Be(@"C:\TestLogs");
        
        settings.ProviderSettings.Should().ContainKey("FilePrefix");
        settings.ProviderSettings["FilePrefix"].Should().Be("test");
        
        settings.ProviderSettings.Should().ContainKey("MaxFileSizeBytes");
        settings.ProviderSettings["MaxFileSizeBytes"].Should().Be(1024 * 1024);
        
        settings.ProviderSettings.Should().ContainKey("RotationInterval");
        settings.ProviderSettings["RotationInterval"].Should().Be(RotationInterval.Daily);
        
        settings.ProviderSettings.Should().ContainKey("UseJsonFormat");
        settings.ProviderSettings["UseJsonFormat"].Should().Be(true);
        
        settings.ProviderSettings.Should().ContainKey("UseUtc");
        settings.ProviderSettings["UseUtc"].Should().Be(false);
    }
    
    [Fact]
    public void EventLogProviderConfiguration_ToLoggingSettings_ShouldConvertCorrectly()
    {
        // Arrange
        var eventLogConfig = new EventLogProviderConfiguration
        {
            Enabled = true,
            Source = "TestApp",
            LogName = "TestLog",
            MachineName = "TestMachine",
            MaxMessageLength = 16384
        };
        
        var globalSettings = new GlobalLoggingSettings { MinimumLevel = LogLevel.Information };
        var categoryOverrides = new System.Collections.Generic.Dictionary<string, LogLevel>();
        
        // Act
        var settings = eventLogConfig.ToLoggingSettings(globalSettings, categoryOverrides);
        
        // Assert
        settings.Should().NotBeNull();
        settings.Enabled.Should().BeTrue();
        
        settings.ProviderSettings.Should().ContainKey("Source");
        settings.ProviderSettings["Source"].Should().Be("TestApp");
        
        settings.ProviderSettings.Should().ContainKey("LogName");
        settings.ProviderSettings["LogName"].Should().Be("TestLog");
        
        settings.ProviderSettings.Should().ContainKey("MachineName");
        settings.ProviderSettings["MachineName"].Should().Be("TestMachine");
        
        settings.ProviderSettings.Should().ContainKey("MaxMessageLength");
        settings.ProviderSettings["MaxMessageLength"].Should().Be(16384);
    }
    
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDirectory))
            {
                Directory.Delete(_testDirectory, true);
            }
        }
        catch { }
    }
}