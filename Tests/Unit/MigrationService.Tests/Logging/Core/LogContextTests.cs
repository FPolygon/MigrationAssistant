using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using MigrationTool.Service.Logging.Core;
using Xunit;

namespace MigrationService.Tests.Logging.Core;

public class LogContextTests
{
    [Fact]
    public void Current_WithNoContext_ShouldReturnNull()
    {
        // Act
        var current = LogContext.Current;

        // Assert
        current.Should().BeNull();
    }

    [Fact]
    public void PushProperty_ShouldCreateNewScope()
    {
        // Act
        using var scope = LogContext.PushProperty("TestKey", "TestValue");

        // Assert
        LogContext.Current.Should().NotBeNull();
        LogContext.Current!.Properties.Should().ContainKey("TestKey");
        LogContext.Current.Properties["TestKey"].Should().Be("TestValue");
    }

    [Fact]
    public void PushProperty_DisposedScope_ShouldRestorePreviousContext()
    {
        // Arrange
        var initialCurrent = LogContext.Current;

        // Act
        using (var scope = LogContext.PushProperty("TestKey", "TestValue"))
        {
            LogContext.Current.Should().NotBeNull();
        }

        // Assert
        LogContext.Current.Should().Be(initialCurrent);
    }

    [Fact]
    public void PushProperties_ShouldCreateScopeWithMultipleProperties()
    {
        // Arrange
        var properties = new Dictionary<string, object?>
        {
            ["Key1"] = "Value1",
            ["Key2"] = 42,
            ["Key3"] = null
        };

        // Act
        using var scope = LogContext.PushProperties(properties);

        // Assert
        LogContext.Current.Should().NotBeNull();
        LogContext.Current!.Properties.Should().HaveCount(3);
        LogContext.Current.Properties["Key1"].Should().Be("Value1");
        LogContext.Current.Properties["Key2"].Should().Be(42);
        LogContext.Current.Properties["Key3"].Should().BeNull();
    }

    [Fact]
    public void PushCorrelationId_ShouldAddCorrelationIdProperty()
    {
        // Arrange
        var correlationId = "test-correlation-123";

        // Act
        using var scope = LogContext.PushCorrelationId(correlationId);

        // Assert
        LogContext.Current.Should().NotBeNull();
        LogContext.Current!.Properties.Should().ContainKey("CorrelationId");
        LogContext.Current.Properties["CorrelationId"].Should().Be(correlationId);
    }

    [Fact]
    public void PushUserContext_ShouldAddUserProperties()
    {
        // Arrange
        var userId = "user123";
        var sessionId = "session456";

        // Act
        using var scope = LogContext.PushUserContext(userId, sessionId);

        // Assert
        LogContext.Current.Should().NotBeNull();
        LogContext.Current!.Properties.Should().ContainKey("UserId");
        LogContext.Current.Properties.Should().ContainKey("SessionId");
        LogContext.Current.Properties["UserId"].Should().Be(userId);
        LogContext.Current.Properties["SessionId"].Should().Be(sessionId);
    }

    [Fact]
    public void PushUserContext_WithoutSessionId_ShouldOnlyAddUserId()
    {
        // Arrange
        var userId = "user123";

        // Act
        using var scope = LogContext.PushUserContext(userId);

        // Assert
        LogContext.Current.Should().NotBeNull();
        LogContext.Current!.Properties.Should().ContainKey("UserId");
        LogContext.Current.Properties.Should().ContainKey("SessionId");
        LogContext.Current.Properties["UserId"].Should().Be(userId);
        LogContext.Current.Properties["SessionId"].Should().BeNull();
    }

    [Fact]
    public void NestedScopes_ShouldMaintainHierarchy()
    {
        // Act & Assert
        using (var outerScope = LogContext.PushProperty("Outer", "OuterValue"))
        {
            LogContext.Current!.Properties.Should().ContainKey("Outer");
            LogContext.Current.Properties["Outer"].Should().Be("OuterValue");
            LogContext.Current.Parent.Should().BeNull();

            using (var innerScope = LogContext.PushProperty("Inner", "InnerValue"))
            {
                LogContext.Current!.Properties.Should().ContainKey("Inner");
                LogContext.Current.Properties["Inner"].Should().Be("InnerValue");
                LogContext.Current.Parent.Should().NotBeNull();
                LogContext.Current.Parent!.Properties.Should().ContainKey("Outer");
            }

            // After inner scope disposed
            LogContext.Current!.Properties.Should().ContainKey("Outer");
            LogContext.Current.Properties.Should().NotContainKey("Inner");
        }

        // After outer scope disposed
        LogContext.Current.Should().BeNull();
    }

    [Fact]
    public void GetProperties_WithNoContext_ShouldReturnEmptyDictionary()
    {
        // Act
        var properties = LogContext.GetProperties();

        // Assert
        properties.Should().NotBeNull();
        properties.Should().BeEmpty();
    }

    [Fact]
    public void GetProperties_WithSingleScope_ShouldReturnScopeProperties()
    {
        // Arrange
        using var scope = LogContext.PushProperty("TestKey", "TestValue");

        // Act
        var properties = LogContext.GetProperties();

        // Assert
        properties.Should().ContainKey("TestKey");
        properties["TestKey"].Should().Be("TestValue");
    }

    [Fact]
    public void GetProperties_WithNestedScopes_ShouldReturnAllProperties()
    {
        // Arrange
        using var outerScope = LogContext.PushProperty("Outer", "OuterValue");
        using var innerScope = LogContext.PushProperty("Inner", "InnerValue");

        // Act
        var properties = LogContext.GetProperties();

        // Assert
        properties.Should().HaveCount(2);
        properties.Should().ContainKey("Outer");
        properties.Should().ContainKey("Inner");
        properties["Outer"].Should().Be("OuterValue");
        properties["Inner"].Should().Be("InnerValue");
    }

    [Fact]
    public void GetProperties_WithOverriddenProperty_ShouldReturnInnerValue()
    {
        // Arrange
        using var outerScope = LogContext.PushProperty("SameKey", "OuterValue");
        using var innerScope = LogContext.PushProperty("SameKey", "InnerValue");

        // Act
        var properties = LogContext.GetProperties();

        // Assert
        properties.Should().ContainKey("SameKey");
        properties["SameKey"].Should().Be("InnerValue"); // Inner value should win
    }

    [Fact]
    public async Task AsyncContext_ShouldFlowAcrossAsyncBoundaries()
    {
        // Arrange
        string? capturedValue = null;

        // Act
        using (var scope = LogContext.PushProperty("AsyncTest", "AsyncValue"))
        {
            await Task.Run(() =>
            {
                var properties = LogContext.GetProperties();
                capturedValue = properties.TryGetValue("AsyncTest", out var value) ? value?.ToString() : null;
            });
        }

        // Assert
        capturedValue.Should().Be("AsyncValue");
    }

    [Fact]
    public void DisposeScope_Multiple_ShouldNotThrow()
    {
        // Arrange
        var scope = LogContext.PushProperty("TestKey", "TestValue");

        // Act & Assert
        var act = () =>
        {
            scope.Dispose();
            scope.Dispose(); // Second dispose should not throw
        };

        act.Should().NotThrow();
    }
}
