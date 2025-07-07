using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MigrationTool.Service.IPC;

public interface IMessageDispatcher
{
    void RegisterHandler(IMessageHandler handler);
    void RegisterHandler(string messageType, IMessageHandler handler);
    Task<IpcMessage?> DispatchAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default);
}

public class MessageDispatcher : IMessageDispatcher
{
    private readonly ILogger<MessageDispatcher> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<string, IMessageHandler> _handlers;

    public MessageDispatcher(ILogger<MessageDispatcher> logger, IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _handlers = new ConcurrentDictionary<string, IMessageHandler>(StringComparer.OrdinalIgnoreCase);
    }

    public void RegisterHandler(IMessageHandler handler)
    {
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        RegisterHandler(handler.MessageType, handler);
    }

    public void RegisterHandler(string messageType, IMessageHandler handler)
    {
        if (string.IsNullOrEmpty(messageType))
        {
            throw new ArgumentNullException(nameof(messageType));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        if (_handlers.TryAdd(messageType, handler))
        {
            _logger.LogInformation("Registered handler for message type: {MessageType}", messageType);
        }
        else
        {
            throw new InvalidOperationException($"Handler for message type {messageType} is already registered");
        }
    }

    public void RegisterHandlers(IEnumerable<IMessageHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            RegisterHandler(handler);
        }
    }

    public void RegisterHandlersFromServiceProvider()
    {
        var handlers = _serviceProvider.GetServices<IMessageHandler>();
        RegisterHandlers(handlers);
    }

    public async Task<IpcMessage?> DispatchAsync(string clientId, IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            throw new ArgumentNullException(nameof(clientId));
        }

        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (!_handlers.TryGetValue(message.Type, out var handler))
        {
            _logger.LogWarning("No handler registered for message type: {MessageType}", message.Type);

            return MessageFactory.CreateAcknowledgment(
                message.Id,
                false,
                $"No handler registered for message type: {message.Type}");
        }

        try
        {
            _logger.LogDebug("Dispatching message {MessageId} of type {MessageType} to handler",
                message.Id, message.Type);

            var response = await handler.HandleAsync(clientId, message, cancellationToken);

            if (response == null)
            {
                // If handler doesn't return a response, send acknowledgment
                response = MessageFactory.CreateAcknowledgment(message.Id, true);
            }

            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message handling cancelled for {MessageId}", message.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching message {MessageId} of type {MessageType}",
                message.Id, message.Type);

            return MessageFactory.CreateAcknowledgment(
                message.Id,
                false,
                $"Error handling message: {ex.Message}");
        }
    }
}

// Extension methods for service registration
public static class MessageDispatcherExtensions
{
    public static IServiceCollection AddIpcMessageHandling(this IServiceCollection services)
    {
        services.AddSingleton<IMessageSerializer, MessageSerializer>();
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddSingleton<IMessageDispatcher, MessageDispatcher>();
        services.AddSingleton<IIpcServer, IpcServer>();

        return services;
    }

    public static IServiceCollection AddMessageHandler<THandler>(this IServiceCollection services)
        where THandler : class, IMessageHandler
    {
        services.AddSingleton<IMessageHandler, THandler>();
        services.AddSingleton<THandler>();

        return services;
    }
}