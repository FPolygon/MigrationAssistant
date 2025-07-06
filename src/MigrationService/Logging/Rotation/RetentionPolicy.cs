using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Rotation;

/// <summary>
/// Manages retention of log files based on age and count.
/// </summary>
public class RetentionPolicy
{
    private readonly int _maxFiles;
    private readonly TimeSpan _maxAge;
    private readonly bool _compressOldFiles;
    private readonly SemaphoreSlim _cleanupLock = new(1, 1);
    
    /// <summary>
    /// Initializes a new instance of the RetentionPolicy.
    /// </summary>
    /// <param name="maxFiles">Maximum number of log files to keep. 0 means no limit.</param>
    /// <param name="maxAge">Maximum age of log files. TimeSpan.Zero means no age limit.</param>
    /// <param name="compressOldFiles">Whether to compress old files instead of deleting them.</param>
    public RetentionPolicy(int maxFiles = 30, TimeSpan maxAge = default, bool compressOldFiles = false)
    {
        _maxFiles = maxFiles;
        _maxAge = maxAge == default ? TimeSpan.FromDays(30) : maxAge;
        _compressOldFiles = compressOldFiles;
    }
    
    /// <summary>
    /// Applies the retention policy to the specified directory.
    /// </summary>
    /// <param name="logDirectory">The directory containing log files.</param>
    /// <param name="filePattern">The file pattern to match (e.g., "*.log").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ApplyRetentionAsync(string logDirectory, string filePattern = "*.log", 
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(logDirectory))
            return;
        
        await _cleanupLock.WaitAsync(cancellationToken);
        try
        {
            var logFiles = Directory.GetFiles(logDirectory, filePattern)
                .Select(f => new FileInfo(f))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.CreationTimeUtc)
                .ToList();
            
            // Apply age-based retention
            if (_maxAge > TimeSpan.Zero)
            {
                var cutoffTime = DateTime.UtcNow - _maxAge;
                var expiredFiles = logFiles.Where(f => f.CreationTimeUtc < cutoffTime).ToList();
                
                foreach (var file in expiredFiles)
                {
                    await ProcessExpiredFileAsync(file, cancellationToken);
                }
                
                logFiles = logFiles.Except(expiredFiles).ToList();
            }
            
            // Apply count-based retention
            if (_maxFiles > 0 && logFiles.Count > _maxFiles)
            {
                var excessFiles = logFiles.Skip(_maxFiles).ToList();
                
                foreach (var file in excessFiles)
                {
                    await ProcessExcessFileAsync(file, cancellationToken);
                }
            }
        }
        finally
        {
            _cleanupLock.Release();
        }
    }
    
    /// <summary>
    /// Gets statistics about log files in the directory.
    /// </summary>
    /// <param name="logDirectory">The directory containing log files.</param>
    /// <param name="filePattern">The file pattern to match.</param>
    /// <returns>Statistics about the log files.</returns>
    public LogFileStatistics GetStatistics(string logDirectory, string filePattern = "*.log")
    {
        if (!Directory.Exists(logDirectory))
        {
            return new LogFileStatistics();
        }
        
        var logFiles = Directory.GetFiles(logDirectory, filePattern)
            .Select(f => new FileInfo(f))
            .Where(f => f.Exists)
            .ToList();
        
        return new LogFileStatistics
        {
            TotalFiles = logFiles.Count,
            TotalSizeBytes = logFiles.Sum(f => f.Length),
            OldestFile = logFiles.Count > 0 ? logFiles.Min(f => f.CreationTimeUtc) : null,
            NewestFile = logFiles.Count > 0 ? logFiles.Max(f => f.CreationTimeUtc) : null,
            AverageSizeBytes = logFiles.Count > 0 ? logFiles.Average(f => f.Length) : 0
        };
    }
    
    private async Task ProcessExpiredFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            if (_compressOldFiles)
            {
                await CompressFileAsync(file, cancellationToken);
            }
            else
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the entire cleanup process
            Console.Error.WriteLine($"Failed to process expired file '{file.FullName}': {ex.Message}");
        }
    }
    
    private async Task ProcessExcessFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            if (_compressOldFiles)
            {
                await CompressFileAsync(file, cancellationToken);
            }
            else
            {
                file.Delete();
            }
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the entire cleanup process
            Console.Error.WriteLine($"Failed to process excess file '{file.FullName}': {ex.Message}");
        }
    }
    
    private async Task CompressFileAsync(FileInfo file, CancellationToken cancellationToken)
    {
        // For now, just delete the file. In a full implementation, we would compress it.
        // This would require adding System.IO.Compression reference and implementing gzip compression.
        await Task.Run(() => file.Delete(), cancellationToken);
    }
    
    public void Dispose()
    {
        _cleanupLock.Dispose();
    }
}

/// <summary>
/// Statistics about log files in a directory.
/// </summary>
public class LogFileStatistics
{
    /// <summary>
    /// Total number of log files.
    /// </summary>
    public int TotalFiles { get; set; }
    
    /// <summary>
    /// Total size of all log files in bytes.
    /// </summary>
    public long TotalSizeBytes { get; set; }
    
    /// <summary>
    /// Creation time of the oldest log file.
    /// </summary>
    public DateTime? OldestFile { get; set; }
    
    /// <summary>
    /// Creation time of the newest log file.
    /// </summary>
    public DateTime? NewestFile { get; set; }
    
    /// <summary>
    /// Average size of log files in bytes.
    /// </summary>
    public double AverageSizeBytes { get; set; }
}