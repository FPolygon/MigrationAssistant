using System;
using System.Threading;
using System.Threading.Tasks;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

public interface IIpcServer : IDisposable
{
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    
    bool IsRunning { get; }
    int ConnectedClients { get; }
    
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(TimeSpan timeout = default);
    Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default);
    Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);
}

public class MessageReceivedEventArgs : EventArgs
{
    public string ClientId { get; }
    public IpcMessage Message { get; }
    public DateTime ReceivedAt { get; }
    
    public MessageReceivedEventArgs(string clientId, IpcMessage message)
    {
        ClientId = clientId;
        Message = message;
        ReceivedAt = DateTime.UtcNow;
    }
}

public class ClientConnectedEventArgs : EventArgs
{
    public string ClientId { get; }
    public DateTime ConnectedAt { get; }
    
    public ClientConnectedEventArgs(string clientId, DateTime connectedAt)
    {
        ClientId = clientId;
        ConnectedAt = connectedAt;
    }
}

public class ClientDisconnectedEventArgs : EventArgs
{
    public string ClientId { get; }
    public DateTime DisconnectedAt { get; }
    public string? Reason { get; }
    
    public ClientDisconnectedEventArgs(string clientId, DateTime disconnectedAt, string? reason = null)
    {
        ClientId = clientId;
        DisconnectedAt = disconnectedAt;
        Reason = reason;
    }
}