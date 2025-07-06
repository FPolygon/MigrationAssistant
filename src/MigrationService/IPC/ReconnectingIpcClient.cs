using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC.Messages;

namespace MigrationTool.Service.IPC;

public interface IReconnectingIpcClient : IIpcClient
{
    int ReconnectAttempts { get; }
    bool IsReconnecting { get; }
    TimeSpan ReconnectDelay { get; set; }
    int MaxReconnectAttempts { get; set; }
    int QueuedMessageCount { get; }
}

public class ReconnectingIpcClient : IReconnectingIpcClient
{
    private readonly ILogger<ReconnectingIpcClient> _logger;
    private readonly IIpcClient _innerClient;
    private readonly ConcurrentQueue<IpcMessage> _messageQueue;
    private readonly SemaphoreSlim _reconnectSemaphore = new(1, 1);
    private readonly Timer _heartbeatTimer;
    
    private CancellationTokenSource? _reconnectCts;
    private int _reconnectAttempts;
    private bool _disposed;
    private long _heartbeatSequence;
    
    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;
    
    public bool IsConnected => _innerClient.IsConnected;
    public string ClientId => _innerClient.ClientId;
    public int ReconnectAttempts => _reconnectAttempts;
    public bool IsReconnecting { get; private set; }
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxReconnectAttempts { get; set; } = 10;
    public int QueuedMessageCount => _messageQueue.Count;
    
    public ReconnectingIpcClient(
        ILogger<ReconnectingIpcClient> logger,
        IIpcClient innerClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _innerClient = innerClient ?? throw new ArgumentNullException(nameof(innerClient));
        _messageQueue = new ConcurrentQueue<IpcMessage>();
        
        _innerClient.MessageReceived += OnInnerMessageReceived;
        _innerClient.ConnectionStateChanged += OnInnerConnectionStateChanged;
        
        // Set up heartbeat timer
        _heartbeatTimer = new Timer(SendHeartbeat, null, Timeout.Infinite, Timeout.Infinite);
    }
    
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ReconnectingIpcClient));
        }
        
        _reconnectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        await ConnectWithRetryAsync(_reconnectCts.Token);
    }
    
    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }
        
        _logger.LogInformation("Disconnecting reconnecting client");
        
        _reconnectCts?.Cancel();
        _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
        
        await _innerClient.DisconnectAsync();
        
        IsReconnecting = false;
        _reconnectAttempts = 0;
    }
    
    public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ReconnectingIpcClient));
        }
        
        if (!IsConnected)
        {
            _logger.LogDebug("Queuing message {MessageType} while disconnected", message.Type);
            _messageQueue.Enqueue(message);
            
            // Trigger reconnection if not already attempting
            if (!IsReconnecting)
            {
                _ = Task.Run(() => ReconnectAsync());
            }
            
            return;
        }
        
        try
        {
            await _innerClient.SendMessageAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message, queuing for retry");
            _messageQueue.Enqueue(message);
            
            // Trigger reconnection
            _ = Task.Run(() => ReconnectAsync());
        }
    }
    
    private async Task ConnectWithRetryAsync(CancellationToken cancellationToken)
    {
        var attempts = 0;
        var baseDelay = ReconnectDelay;
        
        while (!cancellationToken.IsCancellationRequested && attempts < MaxReconnectAttempts)
        {
            try
            {
                _logger.LogInformation("Attempting to connect (attempt {Attempt}/{Max})", 
                    attempts + 1, MaxReconnectAttempts);
                
                await _innerClient.ConnectAsync(cancellationToken);
                
                _reconnectAttempts = 0;
                
                // Start heartbeat
                _heartbeatTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
                
                // Send queued messages
                await SendQueuedMessagesAsync(cancellationToken);
                
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                attempts++;
                _logger.LogWarning(ex, "Connection attempt {Attempt} failed", attempts);
                
                if (attempts >= MaxReconnectAttempts)
                {
                    _logger.LogError("Max reconnection attempts reached");
                    throw;
                }
                
                // Exponential backoff with jitter
                var delay = TimeSpan.FromMilliseconds(
                    baseDelay.TotalMilliseconds * Math.Pow(2, attempts - 1) + 
                    Random.Shared.Next(0, 1000));
                
                if (delay > TimeSpan.FromMinutes(5))
                {
                    delay = TimeSpan.FromMinutes(5);
                }
                
                _logger.LogInformation("Waiting {Delay} before next attempt", delay);
                
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
    }
    
    private async Task ReconnectAsync()
    {
        await _reconnectSemaphore.WaitAsync();
        try
        {
            if (IsConnected || IsReconnecting || _disposed)
            {
                return;
            }
            
            IsReconnecting = true;
            _reconnectAttempts++;
            
            _logger.LogInformation("Starting reconnection attempt {Attempt}", _reconnectAttempts);
            
            if (_reconnectCts == null || _reconnectCts.IsCancellationRequested)
            {
                _reconnectCts = new CancellationTokenSource();
            }
            
            await ConnectWithRetryAsync(_reconnectCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconnection failed");
        }
        finally
        {
            IsReconnecting = false;
            _reconnectSemaphore.Release();
        }
    }
    
    private async Task SendQueuedMessagesAsync(CancellationToken cancellationToken)
    {
        if (_messageQueue.IsEmpty)
        {
            return;
        }
        
        _logger.LogInformation("Sending {Count} queued messages", _messageQueue.Count);
        
        while (_messageQueue.TryDequeue(out var message))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Re-queue the message
                _messageQueue.Enqueue(message);
                break;
            }
            
            try
            {
                await _innerClient.SendMessageAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending queued message");
                
                // Re-queue the message
                _messageQueue.Enqueue(message);
                throw;
            }
        }
    }
    
    private async void SendHeartbeat(object? state)
    {
        if (!IsConnected || _disposed)
        {
            return;
        }
        
        try
        {
            var heartbeat = MessageFactory.CreateHeartbeat(ClientId, Interlocked.Increment(ref _heartbeatSequence));
            await _innerClient.SendMessageAsync(heartbeat);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send heartbeat");
            
            // Trigger reconnection
            _ = Task.Run(() => ReconnectAsync());
        }
    }
    
    private void OnInnerMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        // Filter out heartbeat acknowledgments
        if (e.Message.Type == MessageTypes.Acknowledgment &&
            _innerClient.ClientId == e.ClientId)
        {
            var ack = (e.Message.Payload as AcknowledgmentPayload);
            if (ack?.OriginalMessageId?.StartsWith("heartbeat") == true)
            {
                return;
            }
        }
        
        MessageReceived?.Invoke(this, e);
    }
    
    private void OnInnerConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
    {
        if (!e.IsConnected && !IsReconnecting && !_disposed)
        {
            _logger.LogWarning("Connection lost, initiating reconnection");
            _ = Task.Run(() => ReconnectAsync());
        }
        
        ConnectionStateChanged?.Invoke(this, e);
    }
    
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        
        _disposed = true;
        
        _reconnectCts?.Cancel();
        _heartbeatTimer.Dispose();
        
        _innerClient.MessageReceived -= OnInnerMessageReceived;
        _innerClient.ConnectionStateChanged -= OnInnerConnectionStateChanged;
        _innerClient.Dispose();
        
        _reconnectSemaphore.Dispose();
    }
}