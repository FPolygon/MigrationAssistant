using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.Logging.Rotation;

/// <summary>
/// Rotation strategy that rotates files based on time intervals.
/// </summary>
public class TimeBasedRotationStrategy : IRotationStrategy
{
    private readonly RotationInterval _interval;
    private readonly bool _useUtc;
    private DateTime _lastRotationTime;

    /// <summary>
    /// Initializes a new instance of the TimeBasedRotationStrategy.
    /// </summary>
    /// <param name="interval">The rotation interval.</param>
    /// <param name="useUtc">Whether to use UTC time for rotation.</param>
    public TimeBasedRotationStrategy(RotationInterval interval = RotationInterval.Daily, bool useUtc = true)
    {
        _interval = interval;
        _useUtc = useUtc;
        _lastRotationTime = GetCurrentTime();
    }

    public bool ShouldRotate(string currentFilePath, long currentFileSize)
    {
        var currentTime = GetCurrentTime();
        var shouldRotate = false;

        switch (_interval)
        {
            case RotationInterval.Hourly:
                shouldRotate = currentTime.Hour != _lastRotationTime.Hour ||
                              currentTime.Date != _lastRotationTime.Date;
                break;

            case RotationInterval.Daily:
                shouldRotate = currentTime.Date != _lastRotationTime.Date;
                break;

            case RotationInterval.Weekly:
                var currentWeekStart = GetWeekStart(currentTime);
                var lastWeekStart = GetWeekStart(_lastRotationTime);
                shouldRotate = currentWeekStart != lastWeekStart;
                break;

            case RotationInterval.Monthly:
                shouldRotate = currentTime.Year != _lastRotationTime.Year ||
                              currentTime.Month != _lastRotationTime.Month;
                break;
        }

        if (shouldRotate)
        {
            _lastRotationTime = currentTime;
        }

        return shouldRotate;
    }

    public string GenerateNextFileName(string baseFileName, string extension)
    {
        var timestamp = GetCurrentTime();
        var timeFormat = _interval switch
        {
            RotationInterval.Hourly => "yyyyMMdd_HH",
            RotationInterval.Daily => "yyyyMMdd",
            RotationInterval.Weekly => "yyyyMMdd",
            RotationInterval.Monthly => "yyyyMM",
            _ => "yyyyMMdd"
        };

        return $"{baseFileName}_{timestamp.ToString(timeFormat)}{extension}";
    }

    public Task PostRotationCleanupAsync(string rotatedFilePath, CancellationToken cancellationToken = default)
    {
        // No cleanup needed for time-based rotation
        return Task.CompletedTask;
    }

    private DateTime GetCurrentTime()
    {
        return _useUtc ? DateTime.UtcNow : DateTime.Now;
    }

    private DateTime GetWeekStart(DateTime date)
    {
        var diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return date.AddDays(-diff).Date;
    }
}

/// <summary>
/// Defines the rotation intervals for time-based rotation.
/// </summary>
public enum RotationInterval
{
    Hourly,
    Daily,
    Weekly,
    Monthly
}
