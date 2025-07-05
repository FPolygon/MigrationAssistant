using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace MigrationTool.Service.IPC;

public interface IConnectionManager : IDisposable
{
    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;
    
    int ActiveConnectionCount { get; }
    IEnumerable<string> ActiveClientIds { get; }
    
    Task<IIpcConnection> AddConnectionAsync(string clientId, NamedPipeServerStream pipeStream, CancellationToken cancellationToken = default);
    Task RemoveConnectionAsync(string clientId);
    Task<bool> IsConnectedAsync(string clientId);
    Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default);
    Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);
    Task DisconnectAllAsync();
    IIpcConnection? GetConnection(string clientId);
}