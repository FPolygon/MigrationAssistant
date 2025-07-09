using System;
using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

public class IpcServer : IIpcServer
{
    private readonly ILogger<IpcServer> _logger;
    private readonly IMessageSerializer _serializer;
    private readonly IConnectionManager _connectionManager;
    private readonly string _pipeName;
    private readonly SemaphoreSlim _startStopSemaphore = new(1, 1);

    private CancellationTokenSource? _serverCts;
    private Task? _acceptTask;
    private bool _disposed;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientConnectedEventArgs>? ClientConnected;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public bool IsRunning { get; private set; }
    public int ConnectedClients => _connectionManager.ActiveConnectionCount;

    public IpcServer(
        ILogger<IpcServer> logger,
        IMessageSerializer serializer,
        IConnectionManager connectionManager,
        string? pipeName = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _pipeName = pipeName ?? $"MigrationService_{Environment.MachineName}";

        _connectionManager.MessageReceived += OnConnectionMessageReceived;
        _connectionManager.ClientDisconnected += OnConnectionClientDisconnected;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _startStopSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning)
            {
                _logger.LogWarning("IPC server is already running");
                return;
            }

            _serverCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            IsRunning = true;

            _acceptTask = AcceptConnectionsAsync(_serverCts.Token);

            _logger.LogInformation("IPC server started on pipe: {PipeName}", _pipeName);
        }
        finally
        {
            _startStopSemaphore.Release();
        }
    }

    public async Task StopAsync(TimeSpan timeout = default)
    {
        await _startStopSemaphore.WaitAsync();
        try
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            _serverCts?.Cancel();

            if (_acceptTask != null)
            {
                try
                {
                    if (timeout == default)
                    {
                        timeout = TimeSpan.FromSeconds(30);
                    }

                    await _acceptTask.WaitAsync(timeout);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Timeout waiting for accept task to complete");
                }
            }

            await _connectionManager.DisconnectAllAsync();

            _logger.LogInformation("IPC server stopped");
        }
        finally
        {
            _serverCts?.Dispose();
            _serverCts = null;
            _acceptTask = null;
            _startStopSemaphore.Release();
        }
    }

    public async Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("IPC server is not running");
        }

        await _connectionManager.SendMessageAsync(clientId, message, cancellationToken);
    }

    public async Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("IPC server is not running");
        }

        await _connectionManager.BroadcastMessageAsync(message, cancellationToken);
    }

    private async Task AcceptConnectionsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var pipeServer = CreateNamedPipeServer();

                _logger.LogDebug("Waiting for client connection...");

                await pipeServer.WaitForConnectionAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    pipeServer.Dispose();
                    break;
                }

                _logger.LogInformation("Client connected to named pipe");

                // Handle the connection on a separate task
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await HandleClientConnectionAsync(pipeServer, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error handling client connection");
                    }
                }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");

                // Wait a bit before trying again
                try
                {
                    await Task.Delay(1000, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private NamedPipeServerStream CreateNamedPipeServer()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow authenticated users to connect
        var authenticatedUsers = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            authenticatedUsers,
            PipeAccessRights.ReadWrite,
            AccessControlType.Allow));

        // Allow local system full control
        var localSystem = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            localSystem,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Message,
            PipeOptions.Asynchronous | PipeOptions.WriteThrough,
            inBufferSize: 65536,
            outBufferSize: 65536,
            pipeSecurity);
    }

    private async Task HandleClientConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        var clientId = Guid.NewGuid().ToString();

        try
        {
            var connection = await _connectionManager.AddConnectionAsync(clientId, pipeServer, cancellationToken);

            ClientConnected?.Invoke(this, new ClientConnectedEventArgs(clientId, DateTime.UtcNow));

            await connection.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling client {ClientId}", clientId);
            await _connectionManager.RemoveConnectionAsync(clientId);
        }
    }

    private void OnConnectionMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        MessageReceived?.Invoke(this, e);
    }

    private void OnConnectionClientDisconnected(object? sender, ClientDisconnectedEventArgs e)
    {
        ClientDisconnected?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        StopAsync().GetAwaiter().GetResult();

        _connectionManager.MessageReceived -= OnConnectionMessageReceived;
        _connectionManager.ClientDisconnected -= OnConnectionClientDisconnected;

        _startStopSemaphore.Dispose();
    }
}
