using MigrationTool.Service.Models;

namespace MigrationTool.Service.Core;

public class StateTransitionValidator
{
    // Define valid state transitions
    private static readonly Dictionary<MigrationStateType, HashSet<MigrationStateType>> ValidTransitions = new()
    {
        [MigrationStateType.NotStarted] = new HashSet<MigrationStateType>
        {
            MigrationStateType.Initializing,
            MigrationStateType.Cancelled
        },

        [MigrationStateType.Initializing] = new HashSet<MigrationStateType>
        {
            MigrationStateType.WaitingForUser,
            MigrationStateType.Failed,
            MigrationStateType.Cancelled
        },

        [MigrationStateType.WaitingForUser] = new HashSet<MigrationStateType>
        {
            MigrationStateType.BackupInProgress,
            MigrationStateType.Cancelled,
            MigrationStateType.Escalated
        },

        [MigrationStateType.BackupInProgress] = new HashSet<MigrationStateType>
        {
            MigrationStateType.BackupCompleted,
            MigrationStateType.Failed,
            MigrationStateType.Cancelled,
            MigrationStateType.Escalated
        },

        [MigrationStateType.BackupCompleted] = new HashSet<MigrationStateType>
        {
            MigrationStateType.SyncInProgress,
            MigrationStateType.ReadyForReset,
            MigrationStateType.Failed
        },

        [MigrationStateType.SyncInProgress] = new HashSet<MigrationStateType>
        {
            MigrationStateType.ReadyForReset,
            MigrationStateType.Failed,
            MigrationStateType.Escalated
        },

        [MigrationStateType.ReadyForReset] = new HashSet<MigrationStateType>
        {
            // Terminal state - no transitions except back to failed if something goes wrong
            MigrationStateType.Failed
        },

        [MigrationStateType.Failed] = new HashSet<MigrationStateType>
        {
            MigrationStateType.Initializing, // Allow retry
            MigrationStateType.Cancelled,
            MigrationStateType.Escalated
        },

        [MigrationStateType.Cancelled] = new HashSet<MigrationStateType>
        {
            // Terminal state - no transitions
        },

        [MigrationStateType.Escalated] = new HashSet<MigrationStateType>
        {
            MigrationStateType.Initializing, // Allow retry after IT resolution
            MigrationStateType.Cancelled
        }
    };

    public static bool IsValidTransition(MigrationStateType currentState, MigrationStateType newState)
    {
        if (currentState == newState)
        {
            return true; // Allow staying in the same state
        }

        if (!ValidTransitions.TryGetValue(currentState, out var allowedStates))
        {
            return false;
        }

        return allowedStates.Contains(newState);
    }

    public static string GetInvalidTransitionMessage(MigrationStateType currentState, MigrationStateType newState)
    {
        if (IsValidTransition(currentState, newState))
        {
            return string.Empty;
        }

        return $"Invalid state transition from {currentState} to {newState}. " +
               $"Allowed transitions: {string.Join(", ", GetAllowedTransitions(currentState))}";
    }

    public static IEnumerable<MigrationStateType> GetAllowedTransitions(MigrationStateType currentState)
    {
        if (ValidTransitions.TryGetValue(currentState, out var allowedStates))
        {
            return allowedStates;
        }

        return Enumerable.Empty<MigrationStateType>();
    }

    public static bool IsTerminalState(MigrationStateType state)
    {
        return state == MigrationStateType.ReadyForReset ||
               state == MigrationStateType.Cancelled;
    }

    public static bool RequiresUserAction(MigrationStateType state)
    {
        return state == MigrationStateType.WaitingForUser ||
               state == MigrationStateType.Escalated;
    }

    public static bool IsErrorState(MigrationStateType state)
    {
        return state == MigrationStateType.Failed ||
               state == MigrationStateType.Escalated;
    }

    public static ValidationResult ValidateStateTransition(
        MigrationState currentState,
        MigrationStateType newStateType,
        string reason)
    {
        var result = new ValidationResult { IsValid = true };

        // Validate state-specific constraints first
        if (IsTerminalState(currentState.State) && newStateType != MigrationStateType.Failed)
        {
            result.IsValid = false;
            result.Errors.Add($"Cannot transition from terminal state {currentState.State}");
            return result;
        }

        // Check if transition is allowed
        if (!IsValidTransition(currentState.State, newStateType))
        {
            result.IsValid = false;
            result.Errors.Add(GetInvalidTransitionMessage(currentState.State, newStateType));
            return result;
        }

        // Additional business rule validations
        switch (newStateType)
        {
            case MigrationStateType.BackupInProgress:
                if (string.IsNullOrEmpty(currentState.UserId))
                {
                    result.IsValid = false;
                    result.Errors.Add("Cannot start backup without a valid user ID");
                }
                break;

            case MigrationStateType.ReadyForReset:
                if (currentState.Progress < 100)
                {
                    result.IsValid = false;
                    result.Errors.Add("Cannot mark as ready for reset when progress is less than 100%");
                }
                break;

            case MigrationStateType.Escalated:
                if (string.IsNullOrEmpty(reason))
                {
                    result.IsValid = false;
                    result.Errors.Add("Escalation requires a reason");
                }
                break;

            case MigrationStateType.Failed:
                if (string.IsNullOrEmpty(reason))
                {
                    result.IsValid = false;
                    result.Errors.Add("Failure requires a reason");
                }
                break;
        }

        return result;
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }
}

/// <summary>
/// Represents rules for automatic state transitions
/// </summary>
public class StateTransitionRules
{
    public static MigrationStateType? GetAutomaticTransition(MigrationState state, BackupStatus? backupStatus = null)
    {
        // Automatic transitions based on conditions
        switch (state.State)
        {
            case MigrationStateType.Initializing:
                // Automatically move to WaitingForUser after initialization
                return MigrationStateType.WaitingForUser;

            case MigrationStateType.BackupInProgress:
                if (backupStatus == BackupStatus.Completed)
                {
                    return MigrationStateType.BackupCompleted;
                }
                else if (backupStatus == BackupStatus.Failed)
                {
                    return MigrationStateType.Failed;
                }
                break;

            case MigrationStateType.BackupCompleted:
                // Automatically start sync after backup
                return MigrationStateType.SyncInProgress;

            case MigrationStateType.SyncInProgress:
                if (state.Progress >= 100)
                {
                    return MigrationStateType.ReadyForReset;
                }
                break;
        }

        return null;
    }

    public static bool ShouldEscalate(MigrationState state, TimeSpan elapsed, int errorCount = 0)
    {
        // Escalation rules
        switch (state.State)
        {
            case MigrationStateType.WaitingForUser:
                // Escalate if waiting for more than 48 hours
                return elapsed.TotalHours > 48;

            case MigrationStateType.BackupInProgress:
            case MigrationStateType.SyncInProgress:
                // Escalate if stuck for more than 24 hours
                return elapsed.TotalHours > 24;

            case MigrationStateType.Failed:
                // Escalate after 3 failures
                return errorCount >= 3;

            default:
                return false;
        }
    }

    public static TimeSpan GetStateTimeout(MigrationStateType state)
    {
        return state switch
        {
            MigrationStateType.Initializing => TimeSpan.FromMinutes(5),
            MigrationStateType.WaitingForUser => TimeSpan.FromDays(7),
            MigrationStateType.BackupInProgress => TimeSpan.FromDays(1),
            MigrationStateType.SyncInProgress => TimeSpan.FromHours(6),
            _ => TimeSpan.MaxValue
        };
    }
}