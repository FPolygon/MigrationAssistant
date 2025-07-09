using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC.Handlers;

public class AgentStartedHandler : MessageHandler<AgentStartedPayload>
{
    private readonly IStateManager _stateManager;
    private readonly IConnectionManager _connectionManager;

    public override string MessageType => MessageTypes.AgentStarted;

    public AgentStartedHandler(
        ILogger<AgentStartedHandler> logger,
        IMessageSerializer serializer,
        IStateManager stateManager,
        IConnectionManager connectionManager)
        : base(logger, serializer)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
    }

    public override async Task<IpcMessage?> HandleAsync(
        string clientId,
        AgentStartedPayload payload,
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Agent started for user {UserId} with session {SessionId}",
            payload.UserId, payload.SessionId);

        // Update connection with user ID
        var connection = _connectionManager.GetConnection(clientId);
        connection?.SetUserId(payload.UserId);

        // Check if user has pending backup request
        var migrationState = await _stateManager.GetMigrationStateAsync(payload.UserId, cancellationToken);

        if (migrationState != null && migrationState.State == Models.MigrationStateType.WaitingForUser)
        {
            // Send backup request to the newly connected agent
            var deadline = migrationState.Deadline ?? DateTime.UtcNow.AddDays(7);

            var backupRequest = MessageFactory.CreateBackupRequest(
                payload.UserId,
                "normal",
                deadline,
                "files", "browsers", "email", "system");

            Logger.LogInformation("Sending pending backup request to user {UserId}", payload.UserId);

            return backupRequest;
        }

        // Send current migration status
        var summaries = await _stateManager.GetMigrationSummariesAsync(cancellationToken);
        var readiness = await _stateManager.GetMigrationReadinessAsync(cancellationToken);

        var statusUpdate = MessageFactory.CreateStatusUpdate(
            readiness.CanReset ? "ready" : "waiting",
            readiness.BlockingUserNames,
            readiness.CompletedUserNames,
            readiness.TotalUsers);

        return statusUpdate;
    }
}
