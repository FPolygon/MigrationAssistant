using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.Core;
using MigrationTool.Service.IPC.Messages;
using MigrationTool.Service.Models;

namespace MigrationTool.Service.IPC.Handlers;

public class DelayRequestHandler : MessageHandler<DelayRequestPayload>
{
    private readonly IStateManager _stateManager;
    private readonly IIpcServer _ipcServer;
    private const int MaxDelaysAllowed = 3;
    private const int MaxDelayHours = 72; // 3 days
    
    public override string MessageType => MessageTypes.DelayRequest;
    
    public DelayRequestHandler(
        ILogger<DelayRequestHandler> logger,
        IMessageSerializer serializer,
        IStateManager stateManager,
        IIpcServer ipcServer)
        : base(logger, serializer)
    {
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _ipcServer = ipcServer ?? throw new ArgumentNullException(nameof(ipcServer));
    }
    
    public override async Task<IpcMessage?> HandleAsync(
        string clientId, 
        DelayRequestPayload payload, 
        CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Delay request from user {UserId}: {Reason}, requested delay: {Hours} hours", 
            payload.UserId, payload.Reason, payload.RequestedDelaySeconds / 3600);
        
        // Check if user has already used maximum delays
        var delayCount = await _stateManager.GetUserDelayCountAsync(payload.UserId, cancellationToken);
        
        if (delayCount >= MaxDelaysAllowed)
        {
            Logger.LogWarning("User {UserId} has exceeded maximum delays ({Max})", 
                payload.UserId, MaxDelaysAllowed);
            
            var escalationNotice = MessageFactory.CreateEscalationNotice(
                "max_delays_exceeded",
                $"User has requested more than {MaxDelaysAllowed} delays",
                null);
            
            // Create IT escalation
            await _stateManager.CreateEscalationAsync(new ITEscalation
            {
                UserId = payload.UserId,
                TriggerType = EscalationTriggerType.MaxDelaysExceeded,
                TriggerReason = $"User requested {delayCount + 1} delays (max: {MaxDelaysAllowed})",
                Details = $"Latest reason: {payload.Reason}",
                AutoTriggered = true
            }, cancellationToken);
            
            return escalationNotice;
        }
        
        // Validate requested delay duration
        var requestedHours = payload.RequestedDelaySeconds / 3600;
        if (requestedHours > MaxDelayHours)
        {
            requestedHours = MaxDelayHours;
        }
        
        // Create delay request
        var delayRequest = new DelayRequest
        {
            UserId = payload.UserId,
            RequestedDelayHours = requestedHours,
            Reason = payload.Reason,
            Status = "Approved" // Auto-approve for now
        };
        
        var requestId = await _stateManager.CreateDelayRequestAsync(delayRequest, cancellationToken);
        
        // Calculate new deadline
        var migrationState = await _stateManager.GetMigrationStateAsync(payload.UserId, cancellationToken);
        if (migrationState != null)
        {
            var newDeadline = migrationState.Deadline ?? DateTime.UtcNow;
            newDeadline = newDeadline.AddHours(requestedHours);
            
            // Approve the delay
            await _stateManager.ApproveDelayRequestAsync(requestId, newDeadline, cancellationToken);
            
            Logger.LogInformation("Approved delay for user {UserId}. New deadline: {Deadline}", 
                payload.UserId, newDeadline);
            
            // Send updated backup request with new deadline
            var backupRequest = MessageFactory.CreateBackupRequest(
                payload.UserId,
                "normal",
                newDeadline,
                "files", "browsers", "email", "system");
            
            return backupRequest;
        }
        
        return null;
    }
}