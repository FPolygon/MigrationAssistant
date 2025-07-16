using MigrationTool.Service.Models;

namespace MigrationTool.Service.OneDrive;

/// <summary>
/// Interface for managing quota warnings and escalations
/// </summary>
public interface IQuotaWarningManager
{
    /// <summary>
    /// Checks for warning conditions that should trigger alerts
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of warnings that should be triggered</returns>
    Task<List<QuotaWarning>> CheckForWarningConditionsAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers a quota warning for a user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="level">Warning level</param>
    /// <param name="type">Type of warning</param>
    /// <param name="message">Warning message</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created warning</returns>
    Task<QuotaWarning> TriggerQuotaWarningAsync(string userSid, QuotaWarningLevel level,
        QuotaWarningType type, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Escalates a quota issue to IT support
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="details">Issue details for escalation</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The created escalation record</returns>
    Task<QuotaEscalation> EscalateQuotaIssueAsync(string userSid, QuotaIssueDetails details,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a quota warning
    /// </summary>
    /// <param name="warningId">ID of the warning to resolve</param>
    /// <param name="resolutionNotes">Notes about how the warning was resolved</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ResolveWarningAsync(int warningId, string resolutionNotes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all unresolved warnings for a user
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of unresolved warnings</returns>
    Task<List<QuotaWarning>> GetUnresolvedWarningsAsync(string userSid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs proactive monitoring for all active users
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Number of warnings triggered</returns>
    Task<int> PerformProactiveMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines if an issue should be escalated to IT
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="quotaStatus">Current quota status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if escalation is needed</returns>
    Task<bool> ShouldEscalateAsync(string userSid, QuotaStatus quotaStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates quota issue details for escalation
    /// </summary>
    /// <param name="userSid">The user's security identifier</param>
    /// <param name="quotaStatus">Current quota status</param>
    /// <param name="issueType">Type of issue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Detailed issue information for escalation</returns>
    Task<QuotaIssueDetails> CreateIssueDetailsAsync(string userSid, QuotaStatus quotaStatus,
        QuotaIssueType issueType, CancellationToken cancellationToken = default);
}
