using System;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.IPC;

public interface IIpcConnection : IDisposable
{
    string ClientId { get; }
    bool IsConnected { get; }
    DateTime ConnectedAt { get; }
    DateTime? LastMessageAt { get; }
    string? UserId { get; }
    
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ClientDisconnectedEventArgs>? Disconnected;
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string? reason = null);
    void SetUserId(string userId);
}