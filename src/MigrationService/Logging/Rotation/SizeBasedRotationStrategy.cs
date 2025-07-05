using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Rotation;

/// <summary>
/// Rotation strategy that rotates files based on size.
/// </summary>
public class SizeBasedRotationStrategy : IRotationStrategy
{
    private readonly long _maxFileSizeBytes;
    private int _rotationCounter;
    
    /// <summary>
    /// Initializes a new instance of the SizeBasedRotationStrategy.
    /// </summary>
    /// <param name="maxFileSizeBytes">Maximum file size in bytes before rotation.</param>
    public SizeBasedRotationStrategy(long maxFileSizeBytes = 10 * 1024 * 1024) // 10MB default
    {
        _maxFileSizeBytes = maxFileSizeBytes;
    }
    
    public bool ShouldRotate(string currentFilePath, long currentFileSize)
    {
        return currentFileSize >= _maxFileSizeBytes;
    }
    
    public string GenerateNextFileName(string baseFileName, string extension)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var counter = Interlocked.Increment(ref _rotationCounter);
        
        return $"{baseFileName}_{timestamp}_{counter:D3}{extension}";
    }
    
    public Task PostRotationCleanupAsync(string rotatedFilePath, CancellationToken cancellationToken = default)
    {
        // No cleanup needed for size-based rotation
        return Task.CompletedTask;
    }
}