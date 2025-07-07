using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.IPC.Messages;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.IPC.Handlers;

public class BackupProgressHandler : MessageHandler<BackupProgressPayload>
{
    private readonly IStateManager _stateManager;

    public override string MessageType => MessageTypes.BackupProgress;

    public BackupProgressHandler(
        ILogger<BackupProgressHandler> logger,
        IMessageSerializer serializer,
        IStateManager stateManager)
        : base(logger, serializer)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    public override async Task<IpcMessage?> HandleAsync(
        string clientId,
        BackupProgressPayload payload,
        CancellationToken cancellationToken = default)
    {
        Logger.LogDebug("Backup progress for user {UserId}, category {Category}: {Progress}%",
            payload.UserId, payload.Category, payload.Progress);

        // Find the current backup operation
        var operations = await _stateManager.GetUserBackupOperationsAsync(payload.UserId, cancellationToken);

        BackupOperation? currentOperation = null;
        foreach (var op in operations)
        {
            if (op.Category == payload.Category && op.Status == BackupStatus.InProgress)
            {
                currentOperation = op;
                break;
            }
        }

        if (currentOperation != null)
        {
            // Update backup operation progress
            currentOperation.Progress = (int)payload.Progress;
            currentOperation.BytesTransferred = payload.BytesTransferred;
            currentOperation.BytesTotal = payload.BytesTotal;
            currentOperation.LastUpdated = DateTime.UtcNow;

            await _stateManager.UpdateBackupOperationAsync(currentOperation, cancellationToken);

            // Update overall migration progress
            var migrationState = await _stateManager.GetMigrationStateAsync(payload.UserId, cancellationToken);
            if (migrationState != null)
            {
                // Calculate overall progress across all categories
                var allOperations = await _stateManager.GetUserBackupOperationsAsync(payload.UserId, cancellationToken);

                double totalProgress = 0;
                int categoryCount = 0;

                foreach (var op in allOperations)
                {
                    if (op.Status == BackupStatus.Completed)
                    {
                        totalProgress += 100;
                        categoryCount++;
                    }
                    else if (op.Status == BackupStatus.InProgress)
                    {
                        totalProgress += op.Progress;
                        categoryCount++;
                    }
                    else if (op.Status == BackupStatus.Pending)
                    {
                        categoryCount++;
                    }
                }

                if (categoryCount > 0)
                {
                    migrationState.Progress = (int)(totalProgress / categoryCount);
                    migrationState.LastUpdated = DateTime.UtcNow;
                    await _stateManager.UpdateMigrationStateAsync(migrationState, cancellationToken);
                }
            }
        }
        else
        {
            Logger.LogWarning("No active backup operation found for user {UserId}, category {Category}",
                payload.UserId, payload.Category);
        }

        // Return acknowledgment
        return null; // Will be converted to acknowledgment by dispatcher
    }
}