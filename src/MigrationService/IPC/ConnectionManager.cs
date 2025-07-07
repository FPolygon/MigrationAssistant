using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.IPC;

public class ConnectionManager : IConnectionManager
{
    private readonly ILogger<ConnectionManager> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMessageSerializer _serializer;
    private readonly ConcurrentDictionary<string, IIpcConnection> _connections;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

    private bool _disposed;

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ClientDisconnectedEventArgs>? ClientDisconnected;

    public int ActiveConnectionCount => _connections.Count;
    public IEnumerable<string> ActiveClientIds => _connections.Keys.ToList();

    public ConnectionManager(
        ILogger<ConnectionManager> logger,
        ILoggerFactory loggerFactory,
        IMessageSerializer serializer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _connections = new ConcurrentDictionary<string, IIpcConnection>();
    }

    public async Task<IIpcConnection> AddConnectionAsync(
        string clientId,
        NamedPipeServerStream pipeStream,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ConnectionManager));
        }

        if (string.IsNullOrEmpty(clientId))
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (pipeStream == null)
        {
            throw new ArgumentNullException(nameof(pipeStream));
        }

        await _connectionSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_connections.ContainsKey(clientId))
            {
                throw new InvalidOperationException($"Client {clientId} is already connected");
            }

            var connectionLogger = _loggerFactory.CreateLogger<IpcConnection>();
            var connection = new IpcConnection(clientId, pipeStream, connectionLogger, _serializer);

            connection.MessageReceived += OnConnectionMessageReceived;
            connection.Disconnected += OnConnectionDisconnected;

            if (!_connections.TryAdd(clientId, connection))
            {
                connection.Dispose();
                throw new InvalidOperationException($"Failed to add connection for client {clientId}");
            }

            _logger.LogInformation("Added connection for client {ClientId}. Total connections: {Count}",
                clientId, _connections.Count);

            return connection;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task RemoveConnectionAsync(string clientId)
    {
        if (_disposed)
        {
            return;
        }

        await _connectionSemaphore.WaitAsync();
        try
        {
            if (_connections.TryRemove(clientId, out var connection))
            {
                connection.MessageReceived -= OnConnectionMessageReceived;
                connection.Disconnected -= OnConnectionDisconnected;

                connection.Dispose();

                _logger.LogInformation("Removed connection for client {ClientId}. Total connections: {Count}",
                    clientId, _connections.Count);
            }
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public Task<bool> IsConnectedAsync(string clientId)
    {
        if (_connections.TryGetValue(clientId, out var connection))
        {
            return Task.FromResult(connection.IsConnected);
        }

        return Task.FromResult(false);
    }

    public async Task SendMessageAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(clientId, out var connection))
        {
            throw new InvalidOperationException($"Client {clientId} is not connected");
        }

        if (!connection.IsConnected)
        {
            await RemoveConnectionAsync(clientId);
            throw new InvalidOperationException($"Client {clientId} is no longer connected");
        }

        await connection.SendMessageAsync(message, cancellationToken);
    }

    public async Task BroadcastMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        var connections = _connections.Values.Where(c => c.IsConnected).ToList();

        if (connections.Count == 0)
        {
            _logger.LogDebug("No active connections to broadcast message to");
            return;
        }

        _logger.LogInformation("Broadcasting message {MessageType} to {Count} clients",
            message.Type, connections.Count);

        var tasks = connections.Select(async connection =>
        {
            try
            {
                await connection.SendMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting to client {ClientId}", connection.ClientId);
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task DisconnectAllAsync()
    {
        _logger.LogInformation("Disconnecting all {Count} clients", _connections.Count);

        var connections = _connections.Values.ToList();
        var tasks = connections.Select(async connection =>
        {
            try
            {
                await connection.DisconnectAsync("Server shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disconnecting client {ClientId}", connection.ClientId);
            }
        });

        await Task.WhenAll(tasks);

        _connections.Clear();
    }

    public IIpcConnection? GetConnection(string clientId)
    {
        _connections.TryGetValue(clientId, out var connection);
        return connection;
    }

    private void OnConnectionMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        if (sender is IIpcConnection connection)
        {
            // Update user ID if this is an AgentStarted message
            if (e.Message.Type == MessageTypes.AgentStarted &&
                _serializer.DeserializePayload<Messages.AgentStartedPayload>(e.Message.Payload) is { } agentStarted)
            {
                connection.SetUserId(agentStarted.UserId);
            }
        }

        MessageReceived?.Invoke(this, e);
    }

    private async void OnConnectionDisconnected(object? sender, ClientDisconnectedEventArgs e)
    {
        try
        {
            await RemoveConnectionAsync(e.ClientId);
            ClientDisconnected?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnection for client {ClientId}", e.ClientId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        DisconnectAllAsync().GetAwaiter().GetResult();

        _connectionSemaphore.Dispose();
    }
}