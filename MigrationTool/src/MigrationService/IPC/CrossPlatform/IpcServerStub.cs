#if !WINDOWS && (MACOS || LINUX || CROSS_PLATFORM_BUILD)
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

/// <summary>
/// Cross-platform stub implementation of IpcServer for testing on non-Windows platforms.
/// </summary>
public class IpcServer : IIpcServer
{
    private readonly ILogger<IpcServer> _logger;
    private bool _isRunning;
    private int _connectedClients;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public bool IsRunning => _isRunning;
    public int ConnectedClients => _connectedClients;

    public IpcServer(ILogger<IpcServer> logger)
    {
        _logger = logger;
    }
    
    // Constructor for tests that expect 4 arguments
    public IpcServer(ILogger<IpcServer> logger, IMessageSerializer serializer, IConnectionManager connectionManager, string pipeName)
        : this(logger)
    {
        // Ignore the extra parameters in cross-platform stub
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting cross-platform IPC server stub");
        _isRunning = true;
        
        // Simulate a client connection for testing
        Task.Run(async () =>
        {
            await Task.Delay(100, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                _connectedClients = 1;
                ClientConnected?.Invoke(this, new ClientConnectedEventArgs("test-client", DateTime.UtcNow));
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(TimeSpan timeout = default)
    {
        _logger.LogInformation("Stopping cross-platform IPC server stub");
        _isRunning = false;
        _connectedClients = 0;
        ClientDisconnected?.Invoke(this, new ClientDisconnectedEventArgs("test-client", DateTime.UtcNow, "Server stopped"));
        return Task.CompletedTask;
    }

    public Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending message to client {ClientId}: {MessageType}", clientId, message.Type);
        
        // Simulate message echo for testing
        Task.Run(async () =>
        {
            await Task.Delay(10, cancellationToken);
            if (!cancellationToken.IsCancellationRequested)
            {
                MessageReceived?.Invoke(this, new MessageReceivedEventArgs(clientId, message));
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Broadcasting message: {MessageType}", message.Type);
        return SendMessageAsync("broadcast", message, cancellationToken);
    }

    public void Dispose()
    {
        if (_isRunning)
        {
            StopAsync().Wait(TimeSpan.FromSeconds(5));
        }
    }
}
#endif