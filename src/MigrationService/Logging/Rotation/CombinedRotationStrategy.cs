using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Rotation;

/// <summary>
/// Rotation strategy that combines multiple rotation strategies.
/// Files are rotated when ANY of the strategies determine rotation is needed.
/// </summary>
public class CombinedRotationStrategy : IRotationStrategy
{
    private readonly List<IRotationStrategy> _strategies;
    private readonly IRotationStrategy _primaryStrategy;

    /// <summary>
    /// Initializes a new instance of the CombinedRotationStrategy.
    /// </summary>
    /// <param name="strategies">The rotation strategies to combine.</param>
    /// <param name="primaryStrategy">The primary strategy used for file naming. If null, uses the first strategy.</param>
    public CombinedRotationStrategy(IEnumerable<IRotationStrategy> strategies, IRotationStrategy? primaryStrategy = null)
    {
        _strategies = strategies.ToList();
        if (_strategies.Count == 0)
        {
            throw new ArgumentException("At least one rotation strategy must be provided.", nameof(strategies));
        }

        _primaryStrategy = primaryStrategy ?? _strategies[0];
    }

    /// <summary>
    /// Convenience constructor for size and time-based rotation.
    /// </summary>
    /// <param name="maxFileSizeBytes">Maximum file size in bytes.</param>
    /// <param name="rotationInterval">Time-based rotation interval.</param>
    /// <param name="useUtc">Whether to use UTC time for rotation.</param>
    public CombinedRotationStrategy(long maxFileSizeBytes, RotationInterval rotationInterval, bool useUtc = true)
    {
        _strategies = new List<IRotationStrategy>
        {
            new SizeBasedRotationStrategy(maxFileSizeBytes),
            new TimeBasedRotationStrategy(rotationInterval, useUtc)
        };
        _primaryStrategy = _strategies[1]; // Use time-based for naming
    }

    public bool ShouldRotate(string currentFilePath, long currentFileSize)
    {
        return _strategies.Any(strategy => strategy.ShouldRotate(currentFilePath, currentFileSize));
    }

    public string GenerateNextFileName(string baseFileName, string extension)
    {
        return _primaryStrategy.GenerateNextFileName(baseFileName, extension);
    }

    public async Task PostRotationCleanupAsync(string rotatedFilePath, CancellationToken cancellationToken = default)
    {
        var tasks = _strategies.Select(strategy =>
            strategy.PostRotationCleanupAsync(rotatedFilePath, cancellationToken));

        await Task.WhenAll(tasks);
    }
}
