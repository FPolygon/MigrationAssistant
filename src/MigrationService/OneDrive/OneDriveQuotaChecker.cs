using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Checks OneDrive quota status and performs feasibility analysis
/// </summary>
[SupportedOSPlatform("windows")]
public class OneDriveQuotaChecker : IOneDriveQuotaChecker
{
    private readonly ILogger<OneDriveQuotaChecker> _logger;
    private readonly IOneDriveManager _oneDriveManager;
    private readonly IBackupRequirementsCalculator _requirementsCalculator;
    private readonly IStateManager _stateManager;
    private readonly ServiceConfiguration _configuration;

    public OneDriveQuotaChecker(
        ILogger<OneDriveQuotaChecker> logger,
        IOneDriveManager oneDriveManager,
        IBackupRequirementsCalculator requirementsCalculator,
        IStateManager stateManager,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _oneDriveManager = oneDriveManager;
        _requirementsCalculator = requirementsCalculator;
        _stateManager = stateManager;
        _configuration = configuration.Value;
    }

    /// <inheritdoc/>
    public async Task<QuotaStatus> CheckQuotaStatusAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Checking quota status for user {UserSid}", userSid);

        try
        {
            // Get OneDrive status
            var oneDriveStatus = await _oneDriveManager.GetStatusAsync(userSid, cancellationToken);
            if (!oneDriveStatus.IsInstalled || !oneDriveStatus.IsSignedIn)
            {
                return new QuotaStatus
                {
                    UserId = userSid,
                    HealthLevel = QuotaHealthLevel.Unknown,
                    Issues = { "OneDrive is not properly configured" }
                };
            }

            // Get space information
            var availableSpaceMB = await _oneDriveManager.GetAvailableSpaceMBAsync(userSid, cancellationToken);
            if (availableSpaceMB < 0)
            {
                return new QuotaStatus
                {
                    UserId = userSid,
                    HealthLevel = QuotaHealthLevel.Unknown,
                    Issues = { "Unable to determine OneDrive space availability" }
                };
            }

            // Calculate backup requirements
            var requirements = await _requirementsCalculator.CalculateRequiredSpaceMBAsync(userSid, cancellationToken);
            if (requirements.RequiredSpaceMB < 0)
            {
                return new QuotaStatus
                {
                    UserId = userSid,
                    HealthLevel = QuotaHealthLevel.Unknown,
                    Issues = { "Unable to calculate backup space requirements" }
                };
            }

            // Build quota status
            var quotaStatus = new QuotaStatus
            {
                UserId = userSid,
                AvailableSpaceMB = availableSpaceMB,
                RequiredSpaceMB = requirements.RequiredSpaceMB
            };

            // Get total space from OneDrive account info
            if (oneDriveStatus.AccountInfo != null)
            {
                quotaStatus.TotalSpaceMB = oneDriveStatus.AccountInfo.TotalSpaceBytes.HasValue
                    ? oneDriveStatus.AccountInfo.TotalSpaceBytes.Value / (1024 * 1024)
                    : availableSpaceMB * 10; // Rough estimate if unknown

                quotaStatus.UsedSpaceMB = quotaStatus.TotalSpaceMB - availableSpaceMB;
                quotaStatus.UsagePercentage = quotaStatus.TotalSpaceMB > 0
                    ? (double)quotaStatus.UsedSpaceMB / quotaStatus.TotalSpaceMB * 100
                    : 0;
            }

            // Determine if backup can be accommodated
            quotaStatus.CanAccommodateBackup = availableSpaceMB >= requirements.RequiredSpaceMB;
            quotaStatus.ShortfallMB = quotaStatus.CanAccommodateBackup
                ? 0
                : requirements.RequiredSpaceMB - availableSpaceMB;

            // Assess health level
            quotaStatus.HealthLevel = DetermineHealthLevel(quotaStatus);

            // Generate issues and recommendations
            AnalyzeIssuesAndRecommendations(quotaStatus, requirements.RequiredSpaceMB);

            _logger.LogDebug("Quota status for {UserSid}: Health={HealthLevel}, Available={AvailableMB}MB, " +
                           "Required={RequiredMB}MB, CanAccommodate={CanAccommodate}",
                           userSid, quotaStatus.HealthLevel, quotaStatus.AvailableSpaceMB,
                           quotaStatus.RequiredSpaceMB, quotaStatus.CanAccommodateBackup);

            return quotaStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check quota status for user {UserSid}", userSid);
            return new QuotaStatus
            {
                UserId = userSid,
                HealthLevel = QuotaHealthLevel.Unknown,
                Issues = { $"Error checking quota status: {ex.Message}" }
            };
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateBackupFeasibilityAsync(string userSid, long requiredMB, CancellationToken cancellationToken = default)
    {
        try
        {
            var availableSpaceMB = await _oneDriveManager.GetAvailableSpaceMBAsync(userSid, cancellationToken);
            if (availableSpaceMB < 0)
            {
                _logger.LogWarning("Cannot determine available space for user {UserSid}", userSid);
                return false;
            }

            // Check if we have enough space with minimum free space requirement
            var spaceNeeded = requiredMB + _configuration.QuotaManagement.MinimumFreeSpaceMB;
            var isFeasible = availableSpaceMB >= spaceNeeded;

            _logger.LogDebug("Backup feasibility for {UserSid}: Required={RequiredMB}MB, " +
                           "Available={AvailableMB}MB, Feasible={Feasible}",
                           userSid, requiredMB, availableSpaceMB, isFeasible);

            return isFeasible;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate backup feasibility for user {UserSid}", userSid);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<QuotaHealthLevel> GetQuotaHealthAsync(string userSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var quotaStatus = await CheckQuotaStatusAsync(userSid, cancellationToken);
            return quotaStatus.HealthLevel;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get quota health for user {UserSid}", userSid);
            return QuotaHealthLevel.Unknown;
        }
    }

    /// <inheritdoc/>
    public async Task<long> CalculateSpaceShortfallAsync(string userSid, long requiredMB, CancellationToken cancellationToken = default)
    {
        try
        {
            var availableSpaceMB = await _oneDriveManager.GetAvailableSpaceMBAsync(userSid, cancellationToken);
            if (availableSpaceMB < 0)
            {
                return requiredMB; // Assume worst case
            }

            var totalNeeded = requiredMB + _configuration.QuotaManagement.MinimumFreeSpaceMB;
            return Math.Max(0, totalNeeded - availableSpaceMB);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate space shortfall for user {UserSid}", userSid);
            return requiredMB; // Conservative estimate
        }
    }

    /// <inheritdoc/>
    public List<string> GenerateRecommendations(QuotaStatus quotaStatus, long requiredMB)
    {
        var recommendations = new List<string>();

        if (quotaStatus.HealthLevel == QuotaHealthLevel.Unknown)
        {
            recommendations.Add("Verify OneDrive installation and sign-in status");
            return recommendations;
        }

        if (!quotaStatus.CanAccommodateBackup)
        {
            if (quotaStatus.ShortfallMB > 0)
            {
                recommendations.Add($"Free up at least {quotaStatus.ShortfallMB:N0} MB of OneDrive space");
            }

            if (quotaStatus.UsagePercentage > 90)
            {
                recommendations.Add("Consider upgrading OneDrive storage plan");
                recommendations.Add("Move large files to alternative storage");
                recommendations.Add("Delete unnecessary files from OneDrive");
            }

            recommendations.Add("Review and remove large files that are not essential");
            recommendations.Add("Consider using selective sync to reduce local storage usage");
        }
        else if (quotaStatus.HealthLevel == QuotaHealthLevel.Warning)
        {
            recommendations.Add("Monitor OneDrive usage closely during backup");
            recommendations.Add("Consider cleaning up unnecessary files proactively");
        }

        if (quotaStatus.UsagePercentage > _configuration.QuotaManagement.WarningThresholdPercent)
        {
            recommendations.Add($"OneDrive usage is above {_configuration.QuotaManagement.WarningThresholdPercent}% - consider cleanup");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("OneDrive quota is healthy for backup operations");
        }

        return recommendations;
    }

    /// <inheritdoc/>
    public async Task<bool> IsApproachingQuotaLimitAsync(string userSid, CancellationToken cancellationToken = default)
    {
        try
        {
            var quotaStatus = await CheckQuotaStatusAsync(userSid, cancellationToken);
            return quotaStatus.UsagePercentage >= _configuration.QuotaManagement.WarningThresholdPercent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if user {UserSid} is approaching quota limit", userSid);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<string>> ValidateQuotaConfigurationAsync(string userSid, CancellationToken cancellationToken = default)
    {
        var issues = new List<string>();

        try
        {
            var oneDriveStatus = await _oneDriveManager.GetStatusAsync(userSid, cancellationToken);

            if (!oneDriveStatus.IsInstalled)
            {
                issues.Add("OneDrive for Business is not installed");
            }
            else if (!oneDriveStatus.IsRunning)
            {
                issues.Add("OneDrive for Business is not running");
            }
            else if (!oneDriveStatus.IsSignedIn)
            {
                issues.Add("User is not signed in to OneDrive for Business");
            }

            if (string.IsNullOrEmpty(oneDriveStatus.AccountEmail))
            {
                issues.Add("OneDrive account email is not configured");
            }

            if (string.IsNullOrEmpty(oneDriveStatus.SyncFolder))
            {
                issues.Add("OneDrive sync folder is not configured");
            }

            if (oneDriveStatus.AccountInfo != null)
            {
                if (!oneDriveStatus.AccountInfo.TotalSpaceBytes.HasValue ||
                    oneDriveStatus.AccountInfo.TotalSpaceBytes.Value <= 0)
                {
                    issues.Add("OneDrive total space information is not available");
                }

                if (oneDriveStatus.AccountInfo.HasSyncErrors)
                {
                    issues.Add("OneDrive has active sync errors that may affect quota detection");
                }

                // Check for reasonable quota size (at least 1GB)
                if (oneDriveStatus.AccountInfo.TotalSpaceBytes.HasValue &&
                    oneDriveStatus.AccountInfo.TotalSpaceBytes.Value < 1024 * 1024 * 1024)
                {
                    issues.Add("OneDrive quota appears unusually small (less than 1GB)");
                }
            }

            // Check if available space can be determined
            var availableSpace = await _oneDriveManager.GetAvailableSpaceMBAsync(userSid, cancellationToken);
            if (availableSpace < 0)
            {
                issues.Add("Unable to determine available OneDrive space");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate quota configuration for user {UserSid}", userSid);
            issues.Add($"Error validating quota configuration: {ex.Message}");
        }

        return issues;
    }

    private QuotaHealthLevel DetermineHealthLevel(QuotaStatus quotaStatus)
    {
        // If we can't accommodate backup, it's critical or exceeded
        if (!quotaStatus.CanAccommodateBackup)
        {
            return quotaStatus.UsagePercentage >= 100
                ? QuotaHealthLevel.Exceeded
                : QuotaHealthLevel.Critical;
        }

        // Check usage percentage thresholds
        if (quotaStatus.UsagePercentage >= _configuration.QuotaManagement.CriticalThresholdPercent)
        {
            return QuotaHealthLevel.Critical;
        }

        if (quotaStatus.UsagePercentage >= _configuration.QuotaManagement.WarningThresholdPercent)
        {
            return QuotaHealthLevel.Warning;
        }

        // Check if remaining space after backup would be too low
        var remainingAfterBackup = quotaStatus.AvailableSpaceMB - quotaStatus.RequiredSpaceMB;
        if (remainingAfterBackup < _configuration.QuotaManagement.MinimumFreeSpaceMB)
        {
            return QuotaHealthLevel.Warning;
        }

        return QuotaHealthLevel.Healthy;
    }

    private void AnalyzeIssuesAndRecommendations(QuotaStatus quotaStatus, long requiredMB)
    {
        // Clear existing issues and recommendations
        quotaStatus.Issues.Clear();
        quotaStatus.Recommendations.Clear();

        // Analyze issues
        if (!quotaStatus.CanAccommodateBackup)
        {
            quotaStatus.Issues.Add($"Insufficient OneDrive space: {quotaStatus.ShortfallMB:N0} MB shortfall");
        }

        if (quotaStatus.UsagePercentage >= _configuration.QuotaManagement.CriticalThresholdPercent)
        {
            quotaStatus.Issues.Add($"OneDrive usage is critical: {quotaStatus.UsagePercentage:F1}%");
        }
        else if (quotaStatus.UsagePercentage >= _configuration.QuotaManagement.WarningThresholdPercent)
        {
            quotaStatus.Issues.Add($"OneDrive usage is high: {quotaStatus.UsagePercentage:F1}%");
        }

        var remainingAfterBackup = quotaStatus.AvailableSpaceMB - requiredMB;
        if (remainingAfterBackup < _configuration.QuotaManagement.MinimumFreeSpaceMB)
        {
            quotaStatus.Issues.Add($"Backup would leave insufficient free space: {remainingAfterBackup:N0} MB");
        }

        // Add recommendations
        quotaStatus.Recommendations.AddRange(GenerateRecommendations(quotaStatus, requiredMB));
    }
}
