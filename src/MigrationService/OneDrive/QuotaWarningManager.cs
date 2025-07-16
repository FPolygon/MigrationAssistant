using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Manages quota warnings and escalations for OneDrive space management
/// </summary>
[SupportedOSPlatform("windows")]
public class QuotaWarningManager : IQuotaWarningManager
{
    private readonly ILogger<QuotaWarningManager> _logger;
    private readonly IOneDriveQuotaChecker _quotaChecker;
    private readonly IBackupRequirementsCalculator _requirementsCalculator;
    private readonly IUserProfileManager _profileManager;
    private readonly IStateManager _stateManager;
    private readonly ServiceConfiguration _configuration;

    public QuotaWarningManager(
        ILogger<QuotaWarningManager> logger,
        IOneDriveQuotaChecker quotaChecker,
        IBackupRequirementsCalculator requirementsCalculator,
        IUserProfileManager profileManager,
        IStateManager stateManager,
        IOptions<ServiceConfiguration> configuration)
    {
        _logger = logger;
        _quotaChecker = quotaChecker;
        _requirementsCalculator = requirementsCalculator;
        _profileManager = profileManager;
        _stateManager = stateManager;
        _configuration = configuration.Value;
    }

    /// <inheritdoc/>
    public async Task<List<QuotaWarning>> CheckForWarningConditionsAsync(string userSid, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Checking quota warning conditions for user {UserSid}", userSid);
        var warnings = new List<QuotaWarning>();

        try
        {
            var quotaStatus = await _quotaChecker.CheckQuotaStatusAsync(userSid, cancellationToken);

            // Check various warning conditions
            await CheckHighUsageWarning(userSid, quotaStatus, warnings, cancellationToken);
            await CheckInsufficientSpaceWarning(userSid, quotaStatus, warnings, cancellationToken);
            await CheckBackupTooLargeWarning(userSid, quotaStatus, warnings, cancellationToken);
            await CheckQuotaExceededWarning(userSid, quotaStatus, warnings, cancellationToken);
            await CheckPredictedShortfallWarning(userSid, quotaStatus, warnings, cancellationToken);
            await CheckConfigurationIssues(userSid, quotaStatus, warnings, cancellationToken);

            _logger.LogDebug("Found {WarningCount} warning conditions for user {UserSid}", warnings.Count, userSid);
            return warnings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check warning conditions for user {UserSid}", userSid);
            return warnings;
        }
    }

    /// <inheritdoc/>
    public async Task<QuotaWarning> TriggerQuotaWarningAsync(string userSid, QuotaWarningLevel level,
        QuotaWarningType type, string message, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Triggering quota warning for user {UserSid}: {Level} - {Type}",
            userSid, level, type);

        try
        {
            var quotaStatus = await _quotaChecker.CheckQuotaStatusAsync(userSid, cancellationToken);

            var warning = new QuotaWarning
            {
                UserId = userSid,
                Level = level,
                Type = type,
                Title = GenerateWarningTitle(type, level),
                Message = message,
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            };

            // Save warning to database
            var warningId = await _stateManager.CreateQuotaWarningAsync(warning, cancellationToken);
            warning.Id = warningId;

            // Check if this should trigger escalation
            if (_configuration.QuotaManagement.AutoEscalateCriticalIssues &&
                (level == QuotaWarningLevel.Critical || level == QuotaWarningLevel.Emergency))
            {
                await ConsiderEscalationAsync(userSid, quotaStatus, type, cancellationToken);
            }

            _logger.LogInformation("Created quota warning {WarningId} for user {UserSid}", warningId, userSid);
            return warning;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger quota warning for user {UserSid}", userSid);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<QuotaEscalation> EscalateQuotaIssueAsync(string userSid, QuotaIssueDetails details,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("Escalating quota issue for user {UserSid}: {IssueType}", userSid, details.IssueType);

        try
        {
            var escalation = new QuotaEscalation
            {
                UserId = userSid,
                IssueType = details.IssueType.ToString(),
                Severity = details.Severity.ToString(),
                IssueDescription = details.IssueDescription,
                TechnicalDetails = System.Text.Json.JsonSerializer.Serialize(details.TechnicalDetails),
                RecommendedActions = System.Text.Json.JsonSerializer.Serialize(details.RecommendedActions),
                RequiresImmediateAction = details.RequiresImmediateAction,
                DetectedAt = details.DetectedAt
            };

            var escalationId = await _stateManager.CreateQuotaEscalationAsync(escalation, cancellationToken);
            escalation.Id = escalationId;

            _logger.LogWarning("Created quota escalation {EscalationId} for user {UserSid}", escalationId, userSid);
            return escalation;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to escalate quota issue for user {UserSid}", userSid);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task ResolveWarningAsync(int warningId, string resolutionNotes, CancellationToken cancellationToken = default)
    {
        try
        {
            await _stateManager.ResolveQuotaWarningAsync(warningId, resolutionNotes, cancellationToken);
            _logger.LogInformation("Resolved quota warning {WarningId}: {Notes}", warningId, resolutionNotes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve quota warning {WarningId}", warningId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<List<QuotaWarning>> GetUnresolvedWarningsAsync(string userSid, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _stateManager.GetUnresolvedQuotaWarningsAsync(userSid, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get unresolved warnings for user {UserSid}", userSid);
            return new List<QuotaWarning>();
        }
    }

    /// <inheritdoc/>
    public async Task<int> PerformProactiveMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting proactive quota monitoring for all active users");
        var warningsTriggered = 0;

        try
        {
            var activeProfiles = await _profileManager.GetAllProfilesAsync();
            var activeUsers = activeProfiles.Where(p => p.IsActive).ToList();

            _logger.LogDebug("Monitoring quota for {UserCount} active users", activeUsers.Count);

            foreach (var profile in activeUsers)
            {
                try
                {
                    var warnings = await CheckForWarningConditionsAsync(profile.UserId, cancellationToken);

                    foreach (var warning in warnings)
                    {
                        // Check if we've already warned about this recently
                        var existingWarnings = await GetUnresolvedWarningsAsync(profile.UserId, cancellationToken);
                        var recentSimilarWarning = existingWarnings.FirstOrDefault(w =>
                            w.Type == warning.Type &&
                            w.CreatedAt > DateTime.UtcNow.AddHours(-1)); // Don't spam warnings

                        if (recentSimilarWarning == null)
                        {
                            await TriggerQuotaWarningAsync(profile.UserId, warning.Level,
                                warning.Type, warning.Message, cancellationToken);
                            warningsTriggered++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to monitor quota for user {UserId}", profile.UserId);
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            _logger.LogInformation("Proactive monitoring completed. Triggered {WarningCount} new warnings", warningsTriggered);
            return warningsTriggered;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Proactive monitoring was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform proactive monitoring");
            return warningsTriggered;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ShouldEscalateAsync(string userSid, QuotaStatus quotaStatus, CancellationToken cancellationToken = default)
    {
        try
        {
            // Always escalate if quota is exceeded
            if (quotaStatus.HealthLevel == QuotaHealthLevel.Exceeded)
            {
                return true;
            }

            // Escalate critical issues if auto-escalation is enabled
            if (_configuration.QuotaManagement.AutoEscalateCriticalIssues &&
                quotaStatus.HealthLevel == QuotaHealthLevel.Critical)
            {
                return true;
            }

            // Escalate if backup is not feasible and shortfall is large (>10GB)
            if (!quotaStatus.CanAccommodateBackup && quotaStatus.ShortfallMB > 10240)
            {
                return true;
            }

            // Check for repeated unresolved warnings
            var unresolvedWarnings = await GetUnresolvedWarningsAsync(userSid, cancellationToken);
            var criticalWarnings = unresolvedWarnings.Where(w =>
                w.Level == QuotaWarningLevel.Critical &&
                w.CreatedAt < DateTime.UtcNow.AddHours(-24)).ToList();

            if (criticalWarnings.Count >= 3)
            {
                return true; // Escalate after 3 unresolved critical warnings over 24 hours
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine if escalation is needed for user {UserSid}", userSid);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<QuotaIssueDetails> CreateIssueDetailsAsync(string userSid, QuotaStatus quotaStatus,
        QuotaIssueType issueType, CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _profileManager.GetProfileAsync(userSid);
            var requirements = await _requirementsCalculator.CalculateRequiredSpaceMBAsync(userSid, cancellationToken);

            var details = new QuotaIssueDetails
            {
                UserId = userSid,
                UserDisplayName = profile?.UserName ?? "Unknown User",
                ComputerName = Environment.MachineName,
                IssueType = issueType,
                IssueDescription = GenerateIssueDescription(issueType, quotaStatus),
                CurrentStatus = quotaStatus,
                BackupRequirements = requirements,
                Severity = DetermineSeverity(quotaStatus),
                RequiresImmediateAction = quotaStatus.HealthLevel == QuotaHealthLevel.Exceeded ||
                                        quotaStatus.ShortfallMB > 20480 // >20GB shortfall
            };

            // Add technical details
            details.TechnicalDetails.Add($"OneDrive Usage: {quotaStatus.UsagePercentage:F1}%");
            details.TechnicalDetails.Add($"Available Space: {quotaStatus.AvailableSpaceMB:N0} MB");
            details.TechnicalDetails.Add($"Required Space: {quotaStatus.RequiredSpaceMB:N0} MB");
            details.TechnicalDetails.Add($"Shortfall: {quotaStatus.ShortfallMB:N0} MB");

            if (profile != null)
            {
                details.TechnicalDetails.Add($"Profile Size: {profile.ProfileSizeMB:N0} MB");
                details.TechnicalDetails.Add($"Profile Path: {profile.ProfilePath}");
            }

            // Add recommended actions
            details.RecommendedActions.AddRange(quotaStatus.Recommendations);

            return details;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create issue details for user {UserSid}", userSid);
            throw;
        }
    }

    #region Private Helper Methods

    private async Task CheckHighUsageWarning(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        if (quotaStatus.UsagePercentage >= _configuration.QuotaManagement.WarningThresholdPercent)
        {
            var level = quotaStatus.UsagePercentage >= _configuration.QuotaManagement.CriticalThresholdPercent
                ? QuotaWarningLevel.Critical
                : QuotaWarningLevel.Warning;

            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = level,
                Type = QuotaWarningType.HighUsage,
                Title = "High OneDrive Usage",
                Message = $"OneDrive usage is {quotaStatus.UsagePercentage:F1}% ({quotaStatus.UsedSpaceMB:N0} MB of {quotaStatus.TotalSpaceMB:N0} MB used)",
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            });
        }
    }

    private async Task CheckInsufficientSpaceWarning(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        if (!quotaStatus.CanAccommodateBackup)
        {
            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = QuotaWarningLevel.Critical,
                Type = QuotaWarningType.InsufficientSpace,
                Title = "Insufficient OneDrive Space",
                Message = $"OneDrive does not have enough space for backup. Need {quotaStatus.ShortfallMB:N0} MB more space.",
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            });
        }
    }

    private async Task CheckBackupTooLargeWarning(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        // Warn if backup requirements are more than 50% of total quota
        if (quotaStatus.RequiredSpaceMB > quotaStatus.TotalSpaceMB * 0.5)
        {
            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = QuotaWarningLevel.Warning,
                Type = QuotaWarningType.BackupTooLarge,
                Title = "Large Backup Requirements",
                Message = $"Backup requires {quotaStatus.RequiredSpaceMB:N0} MB, which is {(double)quotaStatus.RequiredSpaceMB / quotaStatus.TotalSpaceMB * 100:F1}% of total OneDrive space",
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            });
        }
    }

    private async Task CheckQuotaExceededWarning(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        if (quotaStatus.HealthLevel == QuotaHealthLevel.Exceeded)
        {
            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = QuotaWarningLevel.Emergency,
                Type = QuotaWarningType.QuotaExceeded,
                Title = "OneDrive Quota Exceeded",
                Message = "OneDrive quota has been exceeded. Immediate action required.",
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            });
        }
    }

    private async Task CheckPredictedShortfallWarning(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        // Predict if backup will cause quota to be exceeded
        var projectedUsage = quotaStatus.UsedSpaceMB + quotaStatus.RequiredSpaceMB;
        if (projectedUsage > quotaStatus.TotalSpaceMB)
        {
            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = QuotaWarningLevel.Critical,
                Type = QuotaWarningType.PredictedShortfall,
                Title = "Predicted Quota Shortfall",
                Message = $"Backup operation will exceed OneDrive quota by {projectedUsage - quotaStatus.TotalSpaceMB:N0} MB",
                CurrentUsageMB = quotaStatus.UsedSpaceMB,
                AvailableSpaceMB = quotaStatus.AvailableSpaceMB,
                RequiredSpaceMB = quotaStatus.RequiredSpaceMB
            });
        }
    }

    private async Task CheckConfigurationIssues(string userSid, QuotaStatus quotaStatus,
        List<QuotaWarning> warnings, CancellationToken cancellationToken)
    {
        if (quotaStatus.HealthLevel == QuotaHealthLevel.Unknown)
        {
            warnings.Add(new QuotaWarning
            {
                UserId = userSid,
                Level = QuotaWarningLevel.Warning,
                Type = QuotaWarningType.ConfigurationIssue,
                Title = "OneDrive Configuration Issue",
                Message = "Unable to determine OneDrive quota status. Please check OneDrive configuration.",
                CurrentUsageMB = 0,
                AvailableSpaceMB = 0,
                RequiredSpaceMB = 0
            });
        }
    }

    private async Task ConsiderEscalationAsync(string userSid, QuotaStatus quotaStatus,
        QuotaWarningType warningType, CancellationToken cancellationToken)
    {
        if (await ShouldEscalateAsync(userSid, quotaStatus, cancellationToken))
        {
            var issueType = warningType switch
            {
                QuotaWarningType.QuotaExceeded => QuotaIssueType.InsufficientQuota,
                QuotaWarningType.InsufficientSpace => QuotaIssueType.InsufficientQuota,
                QuotaWarningType.BackupTooLarge => QuotaIssueType.BackupTooLarge,
                QuotaWarningType.ConfigurationIssue => QuotaIssueType.ConfigurationError,
                _ => QuotaIssueType.InsufficientQuota
            };

            var issueDetails = await CreateIssueDetailsAsync(userSid, quotaStatus, issueType, cancellationToken);
            await EscalateQuotaIssueAsync(userSid, issueDetails, cancellationToken);
        }
    }

    private string GenerateWarningTitle(QuotaWarningType type, QuotaWarningLevel level)
    {
        return type switch
        {
            QuotaWarningType.HighUsage => $"{level} OneDrive Usage",
            QuotaWarningType.InsufficientSpace => "Insufficient OneDrive Space",
            QuotaWarningType.BackupTooLarge => "Large Backup Requirements",
            QuotaWarningType.QuotaExceeded => "OneDrive Quota Exceeded",
            QuotaWarningType.PredictedShortfall => "Predicted Quota Shortfall",
            QuotaWarningType.ConfigurationIssue => "OneDrive Configuration Issue",
            _ => "OneDrive Quota Warning"
        };
    }

    private string GenerateIssueDescription(QuotaIssueType issueType, QuotaStatus quotaStatus)
    {
        return issueType switch
        {
            QuotaIssueType.InsufficientQuota =>
                $"User's OneDrive quota is insufficient for backup operation. Available: {quotaStatus.AvailableSpaceMB:N0} MB, Required: {quotaStatus.RequiredSpaceMB:N0} MB, Shortfall: {quotaStatus.ShortfallMB:N0} MB.",
            QuotaIssueType.BackupTooLarge =>
                $"User's backup requirements ({quotaStatus.RequiredSpaceMB:N0} MB) are exceptionally large and may indicate data cleanup is needed.",
            QuotaIssueType.ConfigurationError =>
                "OneDrive configuration issues are preventing proper quota detection and management.",
            _ => "OneDrive quota issue detected requiring IT intervention."
        };
    }

    private QuotaWarningLevel DetermineSeverity(QuotaStatus quotaStatus)
    {
        return quotaStatus.HealthLevel switch
        {
            QuotaHealthLevel.Exceeded => QuotaWarningLevel.Emergency,
            QuotaHealthLevel.Critical => QuotaWarningLevel.Critical,
            QuotaHealthLevel.Warning => QuotaWarningLevel.Warning,
            _ => QuotaWarningLevel.Info
        };
    }

    #endregion
}
