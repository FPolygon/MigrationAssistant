using Microsoft.Extensions.Logging;
using MigrationTool.Service.Models;
using MigrationTool.Service.ProfileManagement;

namespace MigrationTool.Service.Core;

public class MigrationStateOrchestrator : IMigrationStateOrchestrator
{
    private readonly ILogger<MigrationStateOrchestrator> _logger;
    private readonly IStateManager _stateManager;
    private readonly IUserProfileManager _profileManager;

    public MigrationStateOrchestrator(
        ILogger<MigrationStateOrchestrator> logger,
        IStateManager stateManager,
        IUserProfileManager profileManager)
    {
        _logger = logger;
        _stateManager = stateManager;
        _profileManager = profileManager;
    }

    /// <summary>
    /// Process automatic state transitions for all active migrations
    /// </summary>
    public async Task ProcessAutomaticTransitionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var activeMigrations = await _stateManager.GetActiveMigrationsAsync(cancellationToken);

            foreach (var migration in activeMigrations)
            {
                await ProcessUserTransitionAsync(migration, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing automatic transitions");
        }
    }

    /// <summary>
    /// Process state transition for a specific user
    /// </summary>
    public async Task ProcessUserTransitionAsync(MigrationState migration, CancellationToken cancellationToken)
    {
        try
        {
            // Check for timeout
            var elapsed = DateTime.UtcNow - migration.LastUpdated;
            var timeout = StateTransitionRules.GetStateTimeout(migration.State);

            if (elapsed > timeout)
            {
                _logger.LogWarning("Migration {UserId} has timed out in state {State}",
                    migration.UserId, migration.State);

                await HandleTimeoutAsync(migration, elapsed, cancellationToken);
                return;
            }

            // Check for automatic transitions
            var nextState = await DetermineNextStateAsync(migration, cancellationToken);
            if (nextState.HasValue && nextState.Value != migration.State)
            {
                var reason = $"Automatic transition from {migration.State} to {nextState.Value}";
                var success = await _stateManager.TransitionStateAsync(
                    migration.UserId, nextState.Value, reason, cancellationToken);

                if (success)
                {
                    _logger.LogInformation("User {UserId} automatically transitioned to {State}",
                        migration.UserId, nextState.Value);
                }
            }

            // Check for escalation conditions
            if (await ShouldEscalateAsync(migration, elapsed, cancellationToken))
            {
                var escalationReason = migration.AttentionReason ?? "Automatic escalation based on rules";
                await EscalateToITAsync(migration, escalationReason, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing transition for user {UserId}", migration.UserId);
        }
    }

    /// <summary>
    /// Determine the next state based on current conditions
    /// </summary>
    private async Task<MigrationStateType?> DetermineNextStateAsync(
        MigrationState migration, CancellationToken cancellationToken)
    {
        switch (migration.State)
        {
            case MigrationStateType.BackupInProgress:
                // Check backup status
                var backupOps = await _stateManager.GetUserBackupOperationsAsync(
                    migration.UserId, cancellationToken);

                var latestOp = backupOps
                    .OrderByDescending(op => op.StartedAt)
                    .FirstOrDefault();

                if (latestOp != null)
                {
                    return StateTransitionRules.GetAutomaticTransition(migration, latestOp.Status);
                }
                break;

            case MigrationStateType.SyncInProgress:
                // Check OneDrive sync status
                var syncStatus = await _stateManager.GetOneDriveSyncStatusAsync(
                    migration.UserId, cancellationToken);

                if (syncStatus != null && syncStatus.SyncStatus == "UpToDate" && migration.Progress >= 100)
                {
                    return MigrationStateType.ReadyForReset;
                }
                break;

            default:
                return StateTransitionRules.GetAutomaticTransition(migration);
        }

        return null;
    }

    /// <summary>
    /// Check if migration should be escalated
    /// </summary>
    private async Task<bool> ShouldEscalateAsync(
        MigrationState migration, TimeSpan elapsed, CancellationToken cancellationToken)
    {
        // Check basic escalation rules
        if (StateTransitionRules.ShouldEscalate(migration, elapsed))
        {
            return true;
        }

        // Check for specific conditions
        switch (migration.State)
        {
            case MigrationStateType.BackupInProgress:
            case MigrationStateType.SyncInProgress:
                // Check for repeated failures
                var operations = await _stateManager.GetUserBackupOperationsAsync(
                    migration.UserId, cancellationToken);

                var recentFailures = operations
                    .Where(op => op.Status == BackupStatus.Failed)
                    .Where(op => op.StartedAt > DateTime.UtcNow.AddDays(-1))
                    .Count();

                if (recentFailures >= 3)
                {
                    return true;
                }
                break;

            case MigrationStateType.Failed:
                // Check if already has open escalation
                var escalations = await _stateManager.GetUserEscalationsAsync(
                    migration.UserId, cancellationToken);

                return !escalations.Any(e => e.Status == "Open");
        }

        // Check OneDrive issues
        var syncState = await _stateManager.GetOneDriveSyncStatusAsync(migration.UserId, cancellationToken);
        if (syncState != null)
        {
            // Escalate for quota issues
            if (syncState.QuotaAvailableMB.HasValue && syncState.QuotaAvailableMB < 1000)
            {
                migration.AttentionReason = "OneDrive quota insufficient";
                return true;
            }

            // Escalate for persistent sync errors
            if (syncState.ErrorCount >= 5)
            {
                migration.AttentionReason = "Persistent OneDrive sync errors";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handle timeout for a migration
    /// </summary>
    private async Task HandleTimeoutAsync(
        MigrationState migration, TimeSpan elapsed, CancellationToken cancellationToken)
    {
        var reason = $"Operation timed out after {elapsed.TotalHours:F1} hours in state {migration.State}";

        // Determine appropriate action based on state
        switch (migration.State)
        {
            case MigrationStateType.WaitingForUser:
                // Long timeout is expected, just log
                _logger.LogInformation("User {UserId} has been waiting for {Hours:F1} hours",
                    migration.UserId, elapsed.TotalHours);

                if (elapsed.TotalDays > 7)
                {
                    await EscalateToITAsync(migration, reason, cancellationToken);
                }
                break;

            case MigrationStateType.BackupInProgress:
            case MigrationStateType.SyncInProgress:
                // These should not take this long
                await _stateManager.TransitionStateAsync(
                    migration.UserId, MigrationStateType.Failed, reason, cancellationToken);

                await EscalateToITAsync(migration, reason, cancellationToken);
                break;

            default:
                // Mark as needing attention
                migration.AttentionReason = reason;
                await _stateManager.UpdateMigrationStateAsync(migration, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Escalate migration to IT
    /// </summary>
    private async Task EscalateToITAsync(
        MigrationState migration, string reason, CancellationToken cancellationToken)
    {
        try
        {
            // Create escalation
            var escalation = new ITEscalation
            {
                UserId = migration.UserId,
                TriggerType = DetermineEscalationType(migration, reason),
                TriggerReason = reason,
                Details = $"State: {migration.State}, Progress: {migration.Progress}%, Attention: {migration.AttentionReason}",
                AutoTriggered = true
            };

            var escalationId = await _stateManager.CreateEscalationAsync(escalation, cancellationToken);

            // Transition to escalated state if not already
            if (migration.State != MigrationStateType.Escalated)
            {
                await _stateManager.TransitionStateAsync(
                    migration.UserId, MigrationStateType.Escalated,
                    $"Escalated to IT: {reason}", cancellationToken);
            }

            _logger.LogWarning("Migration {UserId} escalated to IT: {Reason}", migration.UserId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to escalate migration {UserId} to IT", migration.UserId);
        }
    }

    private EscalationTriggerType DetermineEscalationType(MigrationState migration, string reason)
    {
        if (reason.Contains("quota", StringComparison.OrdinalIgnoreCase))
            return EscalationTriggerType.QuotaExceeded;

        if (reason.Contains("sync error", StringComparison.OrdinalIgnoreCase))
            return EscalationTriggerType.SyncError;

        if (reason.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return EscalationTriggerType.Timeout;

        if (migration.State == MigrationStateType.Failed)
            return EscalationTriggerType.BackupFailure;

        return EscalationTriggerType.MultipleFailures;
    }

    /// <summary>
    /// Handle backup completion notification
    /// </summary>
    public async Task HandleBackupCompletedAsync(
        string userId, string operationId, bool success, CancellationToken cancellationToken)
    {
        try
        {
            var migration = await _stateManager.GetMigrationStateAsync(userId, cancellationToken);
            if (migration == null || migration.State != MigrationStateType.BackupInProgress)
            {
                return;
            }

            if (success)
            {
                // Check if all backup categories are complete
                var operations = await _stateManager.GetUserBackupOperationsAsync(userId, cancellationToken);
                var categories = new[] { "files", "browsers", "email", "system" };

                var completedCategories = operations
                    .Where(op => op.Status == BackupStatus.Completed)
                    .Select(op => op.Category.ToLower())
                    .Distinct()
                    .ToHashSet();

                if (categories.All(c => completedCategories.Contains(c)))
                {
                    // All backups complete
                    await _stateManager.TransitionStateAsync(
                        userId, MigrationStateType.BackupCompleted,
                        "All backup categories completed successfully", cancellationToken);
                }
                else
                {
                    // Update progress
                    migration.Progress = (completedCategories.Count * 100) / categories.Length;
                    await _stateManager.UpdateMigrationStateAsync(migration, cancellationToken);
                }
            }
            else
            {
                // Handle backup failure
                var operation = await _stateManager.GetBackupOperationAsync(operationId, cancellationToken);
                if (operation != null && operation.RetryCount >= 3)
                {
                    // Too many retries, fail the migration
                    await _stateManager.TransitionStateAsync(
                        userId, MigrationStateType.Failed,
                        $"Backup operation {operationId} failed after {operation.RetryCount} retries",
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling backup completion for user {UserId}", userId);
        }
    }

    /// <summary>
    /// Get migration statistics
    /// </summary>
    public async Task<MigrationStatistics> GetStatisticsAsync(CancellationToken cancellationToken)
    {
        var stats = new MigrationStatistics();

        try
        {
            var summaries = await _stateManager.GetMigrationSummariesAsync(cancellationToken);

            foreach (var summary in summaries)
            {
                stats.TotalUsers++;

                switch (summary.State)
                {
                    case MigrationStateType.NotStarted:
                        stats.NotStarted++;
                        break;
                    case MigrationStateType.BackupInProgress:
                    case MigrationStateType.SyncInProgress:
                        stats.InProgress++;
                        break;
                    case MigrationStateType.BackupCompleted:
                    case MigrationStateType.ReadyForReset:
                        stats.Completed++;
                        break;
                    case MigrationStateType.Failed:
                        stats.Failed++;
                        break;
                    case MigrationStateType.Escalated:
                        stats.Escalated++;
                        break;
                }

                stats.TotalDataSizeMB += summary.TotalBackupSizeMB;
            }

            stats.CompletionPercentage = stats.TotalUsers > 0
                ? (stats.Completed * 100) / stats.TotalUsers
                : 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating migration statistics");
        }

        return stats;
    }

    /// <summary>
    /// Refresh user profiles and initialize migration states for new users
    /// </summary>
    public async Task RefreshUserProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting user profile refresh");

            // Refresh profiles from Windows
            var updatedCount = await _profileManager.RefreshProfilesAsync(cancellationToken);
            _logger.LogInformation("Updated {Count} user profiles", updatedCount);

            // Get all active profiles that require backup
            var activeProfiles = await _profileManager.GetActiveProfilesRequiringBackupAsync(cancellationToken);
            _logger.LogInformation("Found {Count} active profiles requiring backup", activeProfiles.Count);

            // Initialize migration states for new users
            foreach (var profile in activeProfiles)
            {
                var existingState = await _stateManager.GetMigrationStateAsync(profile.UserId, cancellationToken);
                if (existingState == null)
                {
                    // Create initial migration state for new user
                    var migrationState = new MigrationState
                    {
                        UserId = profile.UserId,
                        State = MigrationStateType.NotStarted,
                        Status = "Active",
                        Progress = 0,
                        StartedAt = DateTime.UtcNow,
                        LastUpdated = DateTime.UtcNow,
                        Deadline = DateTime.UtcNow.AddDays(7), // Default 7-day deadline
                        IsBlocking = true // Active users block reset by default
                    };

                    await _stateManager.UpdateMigrationStateAsync(migrationState, cancellationToken);
                    _logger.LogInformation("Initialized migration state for new user: {UserName} ({UserId})", 
                        profile.UserName, profile.UserId);
                }
            }

            // Check for users who are no longer active
            var allMigrations = await _stateManager.GetActiveMigrationsAsync(cancellationToken);
            var activeUserIds = new HashSet<string>(activeProfiles.Select(p => p.UserId));

            foreach (var migration in allMigrations)
            {
                if (!activeUserIds.Contains(migration.UserId) && migration.IsBlocking)
                {
                    // User is no longer active but still blocking
                    var profile = await _profileManager.GetProfileAsync(migration.UserId, cancellationToken);
                    if (profile != null && !profile.IsActive)
                    {
                        migration.IsBlocking = false;
                        migration.AttentionReason = "User profile is no longer active";
                        await _stateManager.UpdateMigrationStateAsync(migration, cancellationToken);
                        _logger.LogInformation("User {UserId} is no longer active and will not block reset", 
                            migration.UserId);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing user profiles");
        }
    }

    /// <summary>
    /// Initialize migration for all active users
    /// </summary>
    public async Task InitializeMigrationAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Initializing migration for all active users");

            // First refresh profiles to ensure we have latest data
            await RefreshUserProfilesAsync(cancellationToken);

            // Get all active profiles requiring backup
            var activeProfiles = await _profileManager.GetActiveProfilesRequiringBackupAsync(cancellationToken);

            // Transition all users to WaitingForUser state
            foreach (var profile in activeProfiles)
            {
                var state = await _stateManager.GetMigrationStateAsync(profile.UserId, cancellationToken);
                if (state != null && state.State == MigrationStateType.NotStarted)
                {
                    await _stateManager.TransitionStateAsync(
                        profile.UserId, 
                        MigrationStateType.WaitingForUser,
                        "Migration initialized - waiting for user to start backup",
                        cancellationToken);
                }
            }

            _logger.LogInformation("Migration initialized for {Count} users", activeProfiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing migration");
        }
    }

    /// <summary>
    /// Check if all active users have completed their backups
    /// </summary>
    public async Task<MigrationReadinessStatus> GetReadinessStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get readiness from state manager which tracks migration states
            var readiness = await _stateManager.GetMigrationReadinessAsync(cancellationToken);

            // Enhance with profile information
            var activeProfiles = await _profileManager.GetActiveProfilesRequiringBackupAsync(cancellationToken);
            readiness.ActiveUsers = activeProfiles.Count;

            return readiness;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration readiness status");
            return new MigrationReadinessStatus { CanReset = false };
        }
    }
}

public class MigrationStatistics
{
    public int TotalUsers { get; set; }
    public int NotStarted { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Escalated { get; set; }
    public long TotalDataSizeMB { get; set; }
    public int CompletionPercentage { get; set; }
}