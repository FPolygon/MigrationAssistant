using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MigrationTool.Service.IPC;
using MigrationTool.Service.IPC.Messages;
using Moq;
using Xunit;

namespace MigrationService.Tests.IPC;

public class MessageDispatcherTests
{
    private readonly Mock<ILogger<MessageDispatcher>> _loggerMock;
    private readonly ServiceProvider _serviceProvider;
    private readonly MessageDispatcher _dispatcher;
    
    public MessageDispatcherTests()
    {
        _loggerMock = new Mock<ILogger<MessageDispatcher>>();
        
        var services = new ServiceCollection();
        services.AddSingleton(_loggerMock.Object);
        _serviceProvider = services.BuildServiceProvider();
        
        _dispatcher = new MessageDispatcher(_loggerMock.Object, _serviceProvider);
    }
    
    [Fact]
    public void RegisterHandler_ShouldRegisterSuccessfully()
    {
        // Arrange
        var handlerMock = new Mock<IMessageHandler>();
        handlerMock.Setup(x => x.MessageType).Returns(MessageTypes.BackupRequest);
        
        // Act & Assert
        var act = () => _dispatcher.RegisterHandler(handlerMock.Object);
        act.Should().NotThrow();
    }
    
    [Fact]
    public void RegisterHandler_DuplicateType_ShouldThrowException()
    {
        // Arrange
        var handler1 = new Mock<IMessageHandler>();
        handler1.Setup(x => x.MessageType).Returns(MessageTypes.BackupRequest);
        
        var handler2 = new Mock<IMessageHandler>();
        handler2.Setup(x => x.MessageType).Returns(MessageTypes.BackupRequest);
        
        _dispatcher.RegisterHandler(handler1.Object);
        
        // Act & Assert
        var act = () => _dispatcher.RegisterHandler(handler2.Object);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Handler for message type BACKUP_REQUEST is already registered");
    }
    
    [Fact]
    public async Task DispatchAsync_WithRegisteredHandler_ShouldInvokeHandler()
    {
        // Arrange
        var handlerMock = new Mock<IMessageHandler>();
        handlerMock.Setup(x => x.MessageType).Returns(MessageTypes.BackupStarted);
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<IpcMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IpcMessage?)null);
        
        _dispatcher.RegisterHandler(handlerMock.Object);
        
        var message = MessageFactory.CreateBackupStarted("user1", new() { "files" }, 100);
        
        // Act
        var result = await _dispatcher.DispatchAsync("client-123", message);
        
        // Assert
        handlerMock.Verify(x => x.HandleAsync("client-123", message, It.IsAny<CancellationToken>()), Times.Once);
        result.Should().NotBeNull();
        result!.Type.Should().Be(MessageTypes.Acknowledgment);
    }
    
    [Fact]
    public async Task DispatchAsync_WithNoHandler_ShouldReturnErrorAcknowledgment()
    {
        // Arrange
        var message = MessageFactory.CreateBackupStarted("user1", new() { "files" }, 100);
        
        // Act
        var result = await _dispatcher.DispatchAsync("client-123", message);
        
        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(MessageTypes.Acknowledgment);
        
        var ack = result.Payload as AcknowledgmentPayload;
        ack.Should().NotBeNull();
        ack!.Success.Should().BeFalse();
        ack.Error.Should().Contain("No handler registered");
    }
    
    [Fact]
    public async Task DispatchAsync_HandlerThrowsException_ShouldReturnErrorAcknowledgment()
    {
        // Arrange
        var handlerMock = new Mock<IMessageHandler>();
        handlerMock.Setup(x => x.MessageType).Returns(MessageTypes.BackupProgress);
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<IpcMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test error"));
        
        _dispatcher.RegisterHandler(handlerMock.Object);
        
        var message = MessageFactory.CreateBackupProgress("user1", "files", 50, 1000, 2000);
        
        // Act
        var result = await _dispatcher.DispatchAsync("client-123", message);
        
        // Assert
        result.Should().NotBeNull();
        result!.Type.Should().Be(MessageTypes.Acknowledgment);
        
        var ack = result.Payload as AcknowledgmentPayload;
        ack.Should().NotBeNull();
        ack!.Success.Should().BeFalse();
        ack.Error.Should().Contain("Test error");
    }
    
    [Fact]
    public async Task DispatchAsync_HandlerReturnsResponse_ShouldReturnHandlerResponse()
    {
        // Arrange
        var responseMessage = MessageFactory.CreateStatusUpdate("ready", new(), new(), 0);
        
        var handlerMock = new Mock<IMessageHandler>();
        handlerMock.Setup(x => x.MessageType).Returns(MessageTypes.AgentStarted);
        handlerMock.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<IpcMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMessage);
        
        _dispatcher.RegisterHandler(handlerMock.Object);
        
        var message = MessageFactory.CreateAgentStarted("user1", "1.0.0", "session-123");
        
        // Act
        var result = await _dispatcher.DispatchAsync("client-123", message);
        
        // Assert
        result.Should().Be(responseMessage);
    }
}

// Test handler implementation
public class TestMessageHandler : MessageHandler<BackupStartedPayload>
{
    public override string MessageType => MessageTypes.BackupStarted;
    
    public TestMessageHandler(ILogger<TestMessageHandler> logger, IMessageSerializer serializer) 
        : base(logger, serializer)
    {
    }
    
    public override Task<IpcMessage?> HandleAsync(string clientId, BackupStartedPayload payload, CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("Test handler invoked for client {ClientId}", clientId);
        return Task.FromResult<IpcMessage?>(null);
    }
}

public class MessageHandlerTests
{
    [Fact]
    public async Task MessageHandler_ShouldDeserializePayloadAndInvokeTypedHandler()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TestMessageHandler>>();
        var serializer = new MessageSerializer();
        var handler = new TestMessageHandler(loggerMock.Object, serializer);
        
        var message = MessageFactory.CreateBackupStarted("user1", new() { "files" }, 100);
        
        // Act
        var result = await handler.HandleAsync("client-123", message);
        
        // Assert
        loggerMock.Verify(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Test handler invoked")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()
        ), Times.Once);
    }
    
    [Fact]
    public async Task MessageHandler_WithWrongMessageType_ShouldThrowException()
    {
        // Arrange
        var loggerMock = new Mock<ILogger<TestMessageHandler>>();
        var serializer = new MessageSerializer();
        var handler = new TestMessageHandler(loggerMock.Object, serializer);
        
        var message = MessageFactory.CreateBackupProgress("user1", "files", 50, 1000, 2000);
        
        // Act & Assert
        var act = () => handler.HandleAsync("client-123", message);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Handler for BACKUP_STARTED cannot handle message type BACKUP_PROGRESS");
    }
}