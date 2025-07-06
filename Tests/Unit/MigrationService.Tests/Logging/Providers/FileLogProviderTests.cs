using System;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Providers;
using MigrationTool.Service.Logging.Utils;
using Xunit;

namespace MigrationService.Tests.Logging.Providers;

public class FileLogProviderTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly FileLogProvider _provider;
    
    public FileLogProviderTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "MigrationLogTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testDirectory);
        
        _provider = new FileLogProvider(new PlainTextFormatter());
    }
    
    [Fact]
    public void Name_ShouldReturnFileLogger()
    {
        // Assert
        _provider.Name.Should().Be("FileLogger");
    }
    
    [Fact]
    public void IsEnabled_WithValidDirectory_ShouldReturnTrue()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        
        // Act
        _provider.Configure(settings);
        
        // Assert
        _provider.IsEnabled.Should().BeTrue();
    }
    
    [Fact]
    public void IsEnabled_WithEmptyDirectory_ShouldReturnFalse()
    {
        // Arrange
        var settings = CreateFileSettings("");
        
        // Act
        _provider.Configure(settings);
        
        // Assert
        _provider.IsEnabled.Should().BeFalse();
    }
    
    [Fact]
    public void IsLevelEnabled_WithLevelAboveMinimum_ShouldReturnTrue()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        settings.MinimumLevel = LogLevel.Warning;
        _provider.Configure(settings);
        
        // Act & Assert
        _provider.IsLevelEnabled(LogLevel.Error).Should().BeTrue();
        _provider.IsLevelEnabled(LogLevel.Critical).Should().BeTrue();
    }
    
    [Fact]
    public void IsLevelEnabled_WithLevelBelowMinimum_ShouldReturnFalse()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        settings.MinimumLevel = LogLevel.Warning;
        _provider.Configure(settings);
        
        // Act & Assert
        _provider.IsLevelEnabled(LogLevel.Information).Should().BeFalse();
        _provider.IsLevelEnabled(LogLevel.Debug).Should().BeFalse();
    }
    
    [Fact]
    public async Task WriteLogAsync_ShouldCreateLogFile()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        var entry = new LogEntry
        {
            Level = LogLevel.Information,
            Category = "Test",
            Message = "Test message"
        };
        
        // Act
        await _provider.WriteLogAsync(entry);
        await _provider.FlushAsync();
        
        // Dispose provider to ensure file is closed
        _provider.Dispose();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        logFiles.Should().NotBeEmpty();
        
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("Test message");
        logContent.Should().Contain("[INF]");
        logContent.Should().Contain("Test:");
    }
    
    [Fact]
    public async Task WriteLogAsync_WithException_ShouldIncludeExceptionDetails()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        var exception = new InvalidOperationException("Test exception");
        var entry = new LogEntry
        {
            Level = LogLevel.Error,
            Category = "Test",
            Message = "Error occurred",
            Exception = exception
        };
        
        // Act
        await _provider.WriteLogAsync(entry);
        await _provider.FlushAsync();
        
        // Dispose provider to ensure file is closed
        _provider.Dispose();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("Error occurred");
        logContent.Should().Contain("InvalidOperationException");
        logContent.Should().Contain("Test exception");
    }
    
    [Fact]
    public async Task WriteLogAsync_WithProperties_ShouldIncludeProperties()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        var entry = new LogEntry
        {
            Level = LogLevel.Information,
            Category = "Test",
            Message = "Test message",
            Properties = { ["UserId"] = "user123", ["RequestId"] = "req456" }
        };
        
        // Act
        await _provider.WriteLogAsync(entry);
        await _provider.FlushAsync();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("UserId=user123");
        logContent.Should().Contain("RequestId=req456");
    }
    
    [Fact]
    public async Task WriteLogAsync_WithPerformanceMetrics_ShouldIncludeMetrics()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        var entry = new LogEntry
        {
            Level = LogLevel.Information,
            Category = "Performance",
            Message = "Operation completed",
            Performance = new PerformanceMetrics
            {
                DurationMs = 150.5,
                ItemCount = 100,
                MemoryBytes = 1024 * 1024
            }
        };
        
        // Act
        await _provider.WriteLogAsync(entry);
        await _provider.FlushAsync();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("Duration: 150.50ms");
        logContent.Should().Contain("Items: 100");
        logContent.Should().Contain("Memory: 1.0 MB");
    }
    
    [Fact]
    public async Task WriteLogAsync_MultipleEntries_ShouldAppendToSameFile()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        var entry1 = new LogEntry { Level = LogLevel.Information, Message = "First message" };
        var entry2 = new LogEntry { Level = LogLevel.Warning, Message = "Second message" };
        
        // Act
        await _provider.WriteLogAsync(entry1);
        await _provider.WriteLogAsync(entry2);
        await _provider.FlushAsync();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        logFiles.Should().HaveCount(1);
        
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("First message");
        logContent.Should().Contain("Second message");
    }
    
    [Fact]
    public async Task WriteLogAsync_ExceedsMaxFileSize_ShouldRotateFile()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        settings.ProviderSettings["MaxFileSizeBytes"] = 1024L; // 1KB limit
        _provider.Configure(settings);
        
        // Act - Write many entries to exceed file size
        for (int i = 0; i < 100; i++)
        {
            var entry = new LogEntry 
            { 
                Level = LogLevel.Information, 
                Message = $"This is a longer message to help reach the file size limit - Entry {i}" 
            };
            await _provider.WriteLogAsync(entry);
        }
        await _provider.FlushAsync();
        
        // Dispose to ensure all files are closed
        _provider.Dispose();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        logFiles.Should().HaveCountGreaterThan(1); // Should have rotated to multiple files
    }
    
    [Fact]
    public async Task Configure_WithJsonFormatter_ShouldUseJsonFormat()
    {
        // Arrange
        var jsonProvider = new FileLogProvider(new JsonFormatter());
        var settings = CreateFileSettings(_testDirectory);
        settings.ProviderSettings["UseJsonFormat"] = true;
        jsonProvider.Configure(settings);
        
        var entry = new LogEntry
        {
            Level = LogLevel.Information,
            Category = "Test",
            Message = "Test message",
            Properties = { ["TestKey"] = "TestValue" }
        };
        
        // Act
        await jsonProvider.WriteLogAsync(entry);
        await jsonProvider.FlushAsync();
        
        // Assert
        var logFiles = Directory.GetFiles(_testDirectory, "*.log");
        var logContent = await File.ReadAllTextAsync(logFiles[0]);
        logContent.Should().Contain("\"level\":");
        logContent.Should().Contain("\"message\":");
        logContent.Should().Contain("\"testKey\":"); // JSON uses camelCase
        
        // Cleanup
        jsonProvider.Dispose();
    }
    
    [Fact]
    public async Task Dispose_ShouldCloseFileHandles()
    {
        // Arrange
        var settings = CreateFileSettings(_testDirectory);
        _provider.Configure(settings);
        
        // Write something to create the file
        var entry = new LogEntry { Level = LogLevel.Information, Message = "Test" };
        await _provider.WriteLogAsync(entry);
        
        // Act
        _provider.Dispose();
        
        // Assert - Should be able to delete the directory (files are closed)
        var act = () => Directory.Delete(_testDirectory, true);
        act.Should().NotThrow();
        
        // Recreate directory for cleanup in test dispose
        Directory.CreateDirectory(_testDirectory);
    }
    
    private LoggingSettings CreateFileSettings(string directory)
    {
        return new LoggingSettings
        {
            Enabled = true,
            MinimumLevel = LogLevel.Debug,
            ProviderSettings =
            {
                ["LogDirectory"] = directory,
                ["FilePrefix"] = "test",
                ["MaxFileSizeBytes"] = 10 * 1024 * 1024L, // 10MB
                ["UseUtc"] = true,
                ["IncludeTimestamp"] = false
            }
        };
    }
    
    public void Dispose()
    {
        try
        {
            _provider.Dispose();
        }
        catch { }
        
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