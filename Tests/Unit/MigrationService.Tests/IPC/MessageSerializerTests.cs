using System;
using System.Collections.Generic;
using FluentAssertions;
using MigrationTool.Service.IPC;
using MigrationTool.Service.IPC.Messages;
using Xunit;

namespace MigrationService.Tests.IPC;

public class MessageSerializerTests
{
    private readonly MessageSerializer _serializer;
    
    public MessageSerializerTests()
    {
        _serializer = new MessageSerializer();
    }
    
    [Fact]
    public void SerializeMessage_ShouldSerializeBasicMessage()
    {
        // Arrange
        var message = new IpcMessage
        {
            Id = "test-id",
            Type = MessageTypes.Heartbeat,
            Timestamp = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
            Payload = null
        };
        
        // Act
        var json = _serializer.SerializeMessageToString(message);
        
        // Assert
        json.Should().Contain("\"id\":\"test-id\"");
        json.Should().Contain("\"type\":\"HEARTBEAT\"");
        json.Should().Contain("\"timestamp\":\"2024-01-01T12:00:00Z\"");
    }
    
    [Fact]
    public void SerializeMessage_WithBackupRequestPayload_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = MessageFactory.CreateBackupRequest(
            "user-123",
            "high",
            DateTime.UtcNow.AddDays(7),
            "files", "browsers", "email");
        
        // Act
        var json = _serializer.SerializeMessageToString(message);
        
        // Assert
        json.Should().Contain("\"type\":\"BACKUP_REQUEST\"");
        json.Should().Contain("\"userId\":\"user-123\"");
        json.Should().Contain("\"priority\":\"high\"");
        json.Should().Contain("\"categories\":[\"files\",\"browsers\",\"email\"]");
    }
    
    [Fact]
    public void DeserializeMessage_ShouldDeserializeBasicMessage()
    {
        // Arrange
        var json = @"{
            ""id"": ""test-123"",
            ""type"": ""HEARTBEAT"",
            ""timestamp"": ""2024-01-01T12:00:00Z"",
            ""payload"": null
        }";
        
        // Act
        var message = _serializer.DeserializeMessage(json);
        
        // Assert
        message.Should().NotBeNull();
        message!.Id.Should().Be("test-123");
        message.Type.Should().Be(MessageTypes.Heartbeat);
        message.Timestamp.Should().Be(new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc));
    }
    
    [Fact]
    public void DeserializeMessage_WithBackupProgressPayload_ShouldDeserializeCorrectly()
    {
        // Arrange
        var json = @"{
            ""id"": ""msg-456"",
            ""type"": ""BACKUP_PROGRESS"",
            ""timestamp"": ""2024-01-01T12:00:00Z"",
            ""payload"": {
                ""userId"": ""user-789"",
                ""category"": ""files"",
                ""progress"": 45.5,
                ""currentFile"": ""Documents/Report.docx"",
                ""bytesTransferred"": 1073741824,
                ""bytesTotal"": 2147483648
            }
        }";
        
        // Act
        var message = _serializer.DeserializeMessage(json);
        
        // Assert
        message.Should().NotBeNull();
        message!.Type.Should().Be(MessageTypes.BackupProgress);
        
        var payload = message.Payload as BackupProgressPayload;
        payload.Should().NotBeNull();
        payload!.UserId.Should().Be("user-789");
        payload.Category.Should().Be("files");
        payload.Progress.Should().Be(45.5);
        payload.CurrentFile.Should().Be("Documents/Report.docx");
        payload.BytesTransferred.Should().Be(1073741824);
        payload.BytesTotal.Should().Be(2147483648);
    }
    
    [Fact]
    public void RoundTrip_AllMessageTypes_ShouldPreserveData()
    {
        // Test all message types
        var messages = new List<IpcMessage>
        {
            MessageFactory.CreateBackupRequest("user1", "normal", DateTime.UtcNow.AddDays(1), "files"),
            MessageFactory.CreateStatusUpdate("waiting", new List<string> {"user1"}, new List<string>(), 2),
            MessageFactory.CreateEscalationNotice("quota_exceeded", "Not enough space", "INC123"),
            MessageFactory.CreateAgentStarted("user1", "1.0.0", "session-123"),
            MessageFactory.CreateBackupProgress("user1", "files", 50, 1000, 2000, "test.txt"),
            MessageFactory.CreateDelayRequest("user1", "user_busy", 86400, 1),
            MessageFactory.CreateHeartbeat("client-123", 42),
            MessageFactory.CreateAcknowledgment("orig-123", true)
        };
        
        foreach (var original in messages)
        {
            // Act
            var json = _serializer.SerializeMessageToString(original);
            var deserialized = _serializer.DeserializeMessage(json);
            
            // Assert
            deserialized.Should().NotBeNull();
            deserialized!.Id.Should().Be(original.Id);
            deserialized.Type.Should().Be(original.Type);
            deserialized.Payload.Should().NotBeNull();
        }
    }
    
    [Fact]
    public void DeserializePayload_Generic_ShouldWorkCorrectly()
    {
        // Arrange
        var payload = new BackupStartedPayload
        {
            UserId = "test-user",
            Categories = new List<string> { "files", "browsers" },
            EstimatedSizeMB = 1024
        };
        
        // Act
        var deserialized = _serializer.DeserializePayload<BackupStartedPayload>(payload);
        
        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.UserId.Should().Be("test-user");
        deserialized.Categories.Should().BeEquivalentTo(new[] { "files", "browsers" });
        deserialized.EstimatedSizeMB.Should().Be(1024);
    }
    
    [Fact]
    public void DeserializeMessage_WithInvalidJson_ShouldThrowException()
    {
        // Arrange
        var invalidJson = "{ invalid json }";
        
        // Act & Assert
        var act = () => _serializer.DeserializeMessage(invalidJson);
        act.Should().Throw<MessageSerializationException>()
            .WithMessage("Failed to deserialize message JSON");
    }
    
    [Fact]
    public void DeserializeMessage_WithNullResult_ShouldThrowException()
    {
        // Arrange
        var json = "null";
        
        // Act & Assert
        var act = () => _serializer.DeserializeMessage(json);
        act.Should().Throw<MessageSerializationException>()
            .WithMessage("Deserialized message was null");
    }
}