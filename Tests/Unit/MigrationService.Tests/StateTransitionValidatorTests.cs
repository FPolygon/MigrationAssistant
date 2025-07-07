using FluentAssertions;
using MigrationTool.Service.Core;
using MigrationTool.Service.Models;
using Xunit;

namespace MigrationService.Tests;

public class StateTransitionValidatorTests
{
    [Theory]
    [InlineData(MigrationStateType.NotStarted, MigrationStateType.Initializing, true)]
    [InlineData(MigrationStateType.NotStarted, MigrationStateType.BackupInProgress, false)]
    [InlineData(MigrationStateType.Initializing, MigrationStateType.WaitingForUser, true)]
    [InlineData(MigrationStateType.WaitingForUser, MigrationStateType.BackupInProgress, true)]
    [InlineData(MigrationStateType.BackupInProgress, MigrationStateType.BackupCompleted, true)]
    [InlineData(MigrationStateType.BackupCompleted, MigrationStateType.SyncInProgress, true)]
    [InlineData(MigrationStateType.SyncInProgress, MigrationStateType.ReadyForReset, true)]
    [InlineData(MigrationStateType.ReadyForReset, MigrationStateType.Failed, true)]
    [InlineData(MigrationStateType.Failed, MigrationStateType.Initializing, true)]
    [InlineData(MigrationStateType.Cancelled, MigrationStateType.Initializing, false)]
    public void IsValidTransition_ShouldReturnExpectedResult(
        MigrationStateType currentState, MigrationStateType newState, bool expected)
    {
        // Act
        var result = StateTransitionValidator.IsValidTransition(currentState, newState);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsValidTransition_WithSameState_ShouldReturnTrue()
    {
        // Arrange
        var state = MigrationStateType.BackupInProgress;

        // Act
        var result = StateTransitionValidator.IsValidTransition(state, state);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void GetInvalidTransitionMessage_WithValidTransition_ShouldReturnEmpty()
    {
        // Act
        var message = StateTransitionValidator.GetInvalidTransitionMessage(
            MigrationStateType.NotStarted, MigrationStateType.Initializing);

        // Assert
        message.Should().BeEmpty();
    }

    [Fact]
    public void GetInvalidTransitionMessage_WithInvalidTransition_ShouldReturnDescriptiveMessage()
    {
        // Act
        var message = StateTransitionValidator.GetInvalidTransitionMessage(
            MigrationStateType.NotStarted, MigrationStateType.BackupInProgress);

        // Assert
        message.Should().Contain("Invalid state transition");
        message.Should().Contain("NotStarted");
        message.Should().Contain("BackupInProgress");
        message.Should().Contain("Allowed transitions");
    }

    [Fact]
    public void GetAllowedTransitions_ShouldReturnCorrectStates()
    {
        // Act
        var allowed = StateTransitionValidator.GetAllowedTransitions(MigrationStateType.BackupInProgress);

        // Assert
        allowed.Should().Contain(MigrationStateType.BackupCompleted);
        allowed.Should().Contain(MigrationStateType.Failed);
        allowed.Should().Contain(MigrationStateType.Cancelled);
        allowed.Should().Contain(MigrationStateType.Escalated);
        allowed.Should().HaveCount(4);
    }

    [Theory]
    [InlineData(MigrationStateType.ReadyForReset, true)]
    [InlineData(MigrationStateType.Cancelled, true)]
    [InlineData(MigrationStateType.BackupInProgress, false)]
    [InlineData(MigrationStateType.Failed, false)]
    public void IsTerminalState_ShouldReturnExpectedResult(MigrationStateType state, bool expected)
    {
        // Act
        var result = StateTransitionValidator.IsTerminalState(state);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(MigrationStateType.WaitingForUser, true)]
    [InlineData(MigrationStateType.Escalated, true)]
    [InlineData(MigrationStateType.BackupInProgress, false)]
    [InlineData(MigrationStateType.Failed, false)]
    public void RequiresUserAction_ShouldReturnExpectedResult(MigrationStateType state, bool expected)
    {
        // Act
        var result = StateTransitionValidator.RequiresUserAction(state);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(MigrationStateType.Failed, true)]
    [InlineData(MigrationStateType.Escalated, true)]
    [InlineData(MigrationStateType.BackupInProgress, false)]
    [InlineData(MigrationStateType.ReadyForReset, false)]
    public void IsErrorState_ShouldReturnExpectedResult(MigrationStateType state, bool expected)
    {
        // Act
        var result = StateTransitionValidator.IsErrorState(state);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void ValidateStateTransition_WithValidTransition_ShouldReturnValid()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "user123",
            State = MigrationStateType.BackupInProgress,
            Progress = 50
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.BackupCompleted, "Backup completed successfully");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateStateTransition_ToBackupInProgress_WithoutUserId_ShouldFail()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "",
            State = MigrationStateType.WaitingForUser
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.BackupInProgress, "Starting backup");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("valid user ID"));
    }

    [Fact]
    public void ValidateStateTransition_ToReadyForReset_WithLowProgress_ShouldFail()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "user123",
            State = MigrationStateType.SyncInProgress,
            Progress = 75
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.ReadyForReset, "Sync complete");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("progress is less than 100%"));
    }

    [Fact]
    public void ValidateStateTransition_ToEscalated_WithoutReason_ShouldFail()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "user123",
            State = MigrationStateType.Failed
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.Escalated, "");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("requires a reason"));
    }

    [Fact]
    public void ValidateStateTransition_ToFailed_WithoutReason_ShouldFail()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "user123",
            State = MigrationStateType.BackupInProgress
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.Failed, "");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("requires a reason"));
    }

    [Fact]
    public void ValidateStateTransition_FromTerminalState_ShouldFail()
    {
        // Arrange
        var currentState = new MigrationState
        {
            UserId = "user123",
            State = MigrationStateType.Cancelled
        };

        // Act
        var result = StateTransitionValidator.ValidateStateTransition(
            currentState, MigrationStateType.Initializing, "Retry");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("terminal state"));
    }
}

public class StateTransitionRulesTests
{
    [Theory]
    [InlineData(MigrationStateType.Initializing, MigrationStateType.WaitingForUser)]
    [InlineData(MigrationStateType.BackupCompleted, MigrationStateType.SyncInProgress)]
    public void GetAutomaticTransition_ShouldReturnExpectedTransition(
        MigrationStateType currentState, MigrationStateType? expectedNext)
    {
        // Arrange
        var state = new MigrationState { State = currentState };

        // Act
        var result = StateTransitionRules.GetAutomaticTransition(state);

        // Assert
        result.Should().Be(expectedNext);
    }

    [Fact]
    public void GetAutomaticTransition_BackupInProgress_WithCompletedStatus_ShouldReturnBackupCompleted()
    {
        // Arrange
        var state = new MigrationState { State = MigrationStateType.BackupInProgress };

        // Act
        var result = StateTransitionRules.GetAutomaticTransition(state, BackupStatus.Completed);

        // Assert
        result.Should().Be(MigrationStateType.BackupCompleted);
    }

    [Fact]
    public void GetAutomaticTransition_BackupInProgress_WithFailedStatus_ShouldReturnFailed()
    {
        // Arrange
        var state = new MigrationState { State = MigrationStateType.BackupInProgress };

        // Act
        var result = StateTransitionRules.GetAutomaticTransition(state, BackupStatus.Failed);

        // Assert
        result.Should().Be(MigrationStateType.Failed);
    }

    [Fact]
    public void GetAutomaticTransition_SyncInProgress_WithFullProgress_ShouldReturnReadyForReset()
    {
        // Arrange
        var state = new MigrationState
        {
            State = MigrationStateType.SyncInProgress,
            Progress = 100
        };

        // Act
        var result = StateTransitionRules.GetAutomaticTransition(state);

        // Assert
        result.Should().Be(MigrationStateType.ReadyForReset);
    }

    [Theory]
    [InlineData(MigrationStateType.WaitingForUser, 49, 0, true)]
    [InlineData(MigrationStateType.WaitingForUser, 47, 0, false)]
    [InlineData(MigrationStateType.BackupInProgress, 25, 0, true)]
    [InlineData(MigrationStateType.BackupInProgress, 23, 0, false)]
    [InlineData(MigrationStateType.Failed, 1, 3, true)]
    [InlineData(MigrationStateType.Failed, 1, 2, false)]
    public void ShouldEscalate_ShouldReturnExpectedResult(
        MigrationStateType stateType, int elapsedHours, int errorCount, bool expected)
    {
        // Arrange
        var state = new MigrationState { State = stateType };
        var elapsed = TimeSpan.FromHours(elapsedHours);

        // Act
        var result = StateTransitionRules.ShouldEscalate(state, elapsed, errorCount);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(MigrationStateType.Initializing, 5)]
    [InlineData(MigrationStateType.WaitingForUser, 7 * 24 * 60)]
    [InlineData(MigrationStateType.BackupInProgress, 24 * 60)]
    [InlineData(MigrationStateType.SyncInProgress, 6 * 60)]
    public void GetStateTimeout_ShouldReturnExpectedTimeout(
        MigrationStateType state, int expectedMinutes)
    {
        // Act
        var timeout = StateTransitionRules.GetStateTimeout(state);

        // Assert
        timeout.TotalMinutes.Should().Be(expectedMinutes);
    }

    [Fact]
    public void GetStateTimeout_ForUnknownState_ShouldReturnMaxValue()
    {
        // Act
        var timeout = StateTransitionRules.GetStateTimeout(MigrationStateType.ReadyForReset);

        // Assert
        timeout.Should().Be(TimeSpan.MaxValue);
    }
}