using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.Logging.Core;
using MigrationTool.Service.Logging.Utils;

namespace MigrationTool.Service.Logging.Providers;

/// <summary>
/// Logging provider that writes log entries to files with rotation support.
/// </summary>
public class FileLogProvider : ILoggingProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private LoggingSettings _settings = new();
    private FileLogSettings _fileSettings = new();
    private ILogFormatter _formatter;
    private StreamWriter? _currentWriter;
    private string? _currentFilePath;
    private string? _currentFileKey;
    private long _currentFileSize;
    private int _rotationCounter;
    private bool _disposed;

    public string Name => "FileLogger";

    public bool IsEnabled => _settings.Enabled && !string.IsNullOrEmpty(_fileSettings.LogDirectory);

    /// <summary>
    /// Initializes a new instance of the FileLogProvider.
    /// </summary>
    /// <param name="formatter">The log formatter to use. If null, uses PlainTextFormatter.</param>
    public FileLogProvider(ILogFormatter? formatter = null)
    {
        _formatter = formatter ?? new PlainTextFormatter();
    }

    public void Configure(LoggingSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        // Extract file-specific settings
        _fileSettings = new FileLogSettings();

        if (settings.ProviderSettings.TryGetValue("LogDirectory", out var logDir))
        {
            _fileSettings.LogDirectory = logDir?.ToString() ?? "";
        }

        if (settings.ProviderSettings.TryGetValue("FilePrefix", out var prefix))
        {
            _fileSettings.FilePrefix = prefix?.ToString() ?? "migration";
        }

        if (settings.ProviderSettings.TryGetValue("MaxFileSizeBytes", out var maxSize) &&
            maxSize is long sizeBytes)
        {
            _fileSettings.MaxFileSizeBytes = sizeBytes;
        }

        if (settings.ProviderSettings.TryGetValue("UseUtc", out var useUtc) &&
            useUtc is bool utcBool)
        {
            _fileSettings.UseUtc = utcBool;
        }

        if (settings.ProviderSettings.TryGetValue("IncludeTimestamp", out var includeTs) &&
            includeTs is bool tsBool)
        {
            _fileSettings.IncludeTimestamp = tsBool;
        }
    }

    public bool IsLevelEnabled(LogLevel level)
    {
        return level.IsEnabled(_settings.MinimumLevel);
    }

    public async Task WriteLogAsync(LogEntry entry, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || _disposed)
        {
            return;
        }

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureWriterAsync(cancellationToken);

            if (_currentWriter != null)
            {
                var formattedLog = _formatter.Format(entry);
                await _currentWriter.WriteLineAsync(formattedLog.AsMemory(), cancellationToken);
                await _currentWriter.FlushAsync();

                _currentFileSize += Encoding.UTF8.GetByteCount(formattedLog) + Environment.NewLine.Length;

                // Check if rotation is needed
                if (_currentFileSize >= _fileSettings.MaxFileSizeBytes)
                {
                    await RotateFileAsync(cancellationToken);
                }
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_currentWriter != null && !_disposed)
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                await _currentWriter.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }
    }

    private async Task EnsureWriterAsync(CancellationToken cancellationToken)
    {
        if (_currentWriter != null && _currentFilePath != null)
        {
            // Check if we need to rotate based on date change
            var timestamp = _fileSettings.UseUtc ? DateTime.UtcNow : DateTime.Now;
            var currentDateKey = timestamp.ToString("yyyyMMdd");

            // Only check for date-based rotation if we're not using timestamps
            if (!_fileSettings.IncludeTimestamp && _currentFileKey != null && !_currentFileKey.StartsWith(currentDateKey))
            {
                // Reset for new day
                _currentFileKey = null;
                _rotationCounter = 0;
                await RotateFileAsync(cancellationToken);
            }
        }

        if (_currentWriter == null)
        {
            await OpenNewFileAsync(cancellationToken);
        }
    }

    private async Task OpenNewFileAsync(CancellationToken cancellationToken)
    {
        // Ensure directory exists
        Directory.CreateDirectory(_fileSettings.LogDirectory);

        // Use forceNewFile=true if we have a rotation counter > 0 (indicating rotation)
        var fileName = GenerateFileName(_rotationCounter > 0);
        _currentFilePath = Path.Combine(_fileSettings.LogDirectory, fileName);

        // Open file for append, create if doesn't exist
        var fileStream = new FileStream(
            _currentFilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        _currentWriter = new StreamWriter(fileStream, Encoding.UTF8);
        _currentFileSize = fileStream.Length;

        // Write header if new file
        if (_currentFileSize == 0)
        {
            await WriteHeaderAsync(cancellationToken);
        }
    }

    private async Task WriteHeaderAsync(CancellationToken cancellationToken)
    {
        if (_currentWriter == null)
        {
            return;
        }

        var header = new StringBuilder();
        header.AppendLine($"# Migration Tool Log File");
        header.AppendLine($"# Created: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        header.AppendLine($"# Machine: {Environment.MachineName}");
        header.AppendLine($"# Process: {Environment.ProcessId}");
        header.AppendLine(new string('-', 80));

        await _currentWriter.WriteLineAsync(header.ToString().AsMemory(), cancellationToken);
        await _currentWriter.FlushAsync();

        _currentFileSize += Encoding.UTF8.GetByteCount(header.ToString());
    }

    private async Task RotateFileAsync(CancellationToken cancellationToken)
    {
        if (_currentWriter != null)
        {
            await _currentWriter.FlushAsync();
            await _currentWriter.DisposeAsync();
            _currentWriter = null;
        }

        _currentFilePath = null;
        _currentFileSize = 0;
        _rotationCounter++;
    }

    private string GenerateFileName(bool forceNewFile = false)
    {
        var timestamp = _fileSettings.UseUtc ? DateTime.UtcNow : DateTime.Now;

        if (_fileSettings.IncludeTimestamp)
        {
            // When rotating, add rotation counter to ensure uniqueness
            if (forceNewFile && _rotationCounter > 0)
            {
                return $"{_fileSettings.FilePrefix}_{timestamp:yyyyMMdd_HHmmss}_{_rotationCounter:D3}.log";
            }
            return $"{_fileSettings.FilePrefix}_{timestamp:yyyyMMdd_HHmmss}.log";
        }
        else
        {
            // Generate a unique key for the current log session (per day)
            var dateKey = timestamp.ToString("yyyyMMdd");

            // If we don't have a current file key for today, generate one
            if (_currentFileKey == null || !_currentFileKey.StartsWith(dateKey))
            {
                var uniqueId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();
                _currentFileKey = $"{dateKey}_{uniqueId}";
            }

            // When rotating, add rotation counter
            if (forceNewFile && _rotationCounter > 0)
            {
                return $"{_fileSettings.FilePrefix}_{_currentFileKey}_{_rotationCounter:D3}.log";
            }

            return $"{_fileSettings.FilePrefix}_{_currentFileKey}.log";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _currentWriter?.Dispose();
        }
        catch { }

        _writeLock.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_currentWriter != null)
        {
            try
            {
                await _currentWriter.FlushAsync();
                await _currentWriter.DisposeAsync();
            }
            catch { }
        }

        _writeLock.Dispose();
    }
}

/// <summary>
/// Settings specific to file logging.
/// </summary>
public class FileLogSettings
{
    /// <summary>
    /// Directory where log files are stored.
    /// </summary>
    public string LogDirectory { get; set; } = @"C:\ProgramData\MigrationTool\Logs";

    /// <summary>
    /// Prefix for log file names.
    /// </summary>
    public string FilePrefix { get; set; } = "migration";

    /// <summary>
    /// Maximum size of a log file before rotation (default: 10MB).
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Whether to use UTC timestamps in file names and logs.
    /// </summary>
    public bool UseUtc { get; set; } = true;

    /// <summary>
    /// Whether to include timestamp in file names.
    /// </summary>
    public bool IncludeTimestamp { get; set; } = false;
}
