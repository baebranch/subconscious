using System.Net.WebSockets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Subconscious.Engine.Api.Sessions;
using Xunit;

namespace Subconscious.Engine.Tests.Api.Sessions;

public class SessionManagerTests
{
    private static SessionManager CreateManager() => new(NullLogger<SessionManager>.Instance);

    [Fact]
    public void CreateSession_CreatesNewSession()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var session = manager.CreateSession(webSocket: null!);

        // Assert
        session.Should().NotBeNull();
        session.SessionId.Should().NotBeNullOrEmpty();
        session.ConnectedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        session.LastActivityAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void GetSession_WithValidId_ReturnsSession()
    {
        // Arrange
        var manager = CreateManager();
        var created = manager.CreateSession(webSocket: null!);

        // Act
        var retrieved = manager.GetSession(created.SessionId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Should().BeSameAs(created);
    }

    [Fact]
    public void GetSession_WithInvalidId_ReturnsNull()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var retrieved = manager.GetSession("nonexistent");

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public void RemoveSession_WithValidId_RemovesSession()
    {
        // Arrange
        var manager = CreateManager();
        var created = manager.CreateSession(webSocket: null!);

        // Act
        manager.RemoveSession(created.SessionId);

        // Assert
        manager.GetSession(created.SessionId).Should().BeNull();
    }

    [Fact]
    public void RemoveSession_WithInvalidId_DoesNotThrow()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var act = () => manager.RemoveSession("nonexistent");

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetActiveSessions_ReturnsAllActiveSessions()
    {
        // Arrange
        var manager = CreateManager();
        manager.CreateSession(webSocket: null!);
        manager.CreateSession(webSocket: null!);
        manager.CreateSession(webSocket: null!);

        // Act
        var sessions = manager.GetActiveSessions();

        // Assert
        sessions.Should().HaveCount(3);
    }

    [Fact]
    public void GetActiveSessions_AfterRemoval_ReturnsCorrectCount()
    {
        // Arrange
        var manager = CreateManager();
        var first = manager.CreateSession(webSocket: null!);
        manager.CreateSession(webSocket: null!);
        manager.RemoveSession(first.SessionId);

        // Act
        var sessions = manager.GetActiveSessions();

        // Assert
        sessions.Should().HaveCount(1);
    }

    [Fact]
    public void ConcurrentOperations_ThreadSafe()
    {
        // Arrange
        var manager = CreateManager();
        const int threadCount = 10;
        const int operationsPerThread = 100;

        // Act - Create sessions concurrently
        var tasks = Enumerable.Range(0, threadCount)
            .Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    manager.CreateSession(webSocket: null!);
                }
            }))
            .ToArray();

        Task.WaitAll(tasks);

        // Assert
        var sessions = manager.GetActiveSessions();
        sessions.Should().HaveCount(threadCount * operationsPerThread);
    }

    [Fact]
    public void AgentSession_RegisterClientTool_Works()
    {
        // Arrange
        var manager = CreateManager();
        var session = manager.CreateSession(webSocket: null!);

        // Act
        session.RegisterClientTool("custom_tool", "A custom tool");

        // Assert
        session.RegisteredTools.Should().ContainKey("custom_tool");
        session.RegisteredTools["custom_tool"].Name.Should().Be("custom_tool");
        session.RegisteredTools["custom_tool"].Description.Should().Be("A custom tool");
    }

    [Fact]
    public void AgentSession_GetAvailableTools_IncludesClientTools()
    {
        // Arrange
        var manager = CreateManager();
        var session = manager.CreateSession(webSocket: null!);
        session.RegisterClientTool("tool1", "Tool 1");
        session.RegisterClientTool("tool2", "Tool 2");

        // Act
        var tools = session.GetAvailableTools().ToList();

        // Assert
        tools.Should().Contain("tool1");
        tools.Should().Contain("tool2");
        tools.Should().Contain("get_current_time"); // Built-in
    }

    [Fact]
    public void AgentSession_SetMetadata_Works()
    {
        // Arrange
        var manager = CreateManager();
        var session = manager.CreateSession(webSocket: null!);

        // Act
        session.SetMetadata("clientId", "vscode-ext");
        session.SetMetadata("clientVersion", "1.0.0");

        // Assert
        session.GetMetadata("clientId").Should().Be("vscode-ext");
        session.GetMetadata("clientVersion").Should().Be("1.0.0");
        session.GetMetadata("nonexistent").Should().BeNull();
    }

    [Fact]
    public void AgentSession_LastActivityAt_UpdatesOnActivity()
    {
        // Arrange
        var manager = CreateManager();
        var session = manager.CreateSession(webSocket: null!);
        var initialActivity = session.LastActivityAt;

        // Act
        Thread.Sleep(50); // Wait a bit
        session.SetMetadata("key", "value");

        // Assert
        session.LastActivityAt.Should().BeAfter(initialActivity);
    }

    [Fact]
    public async Task CleanupOrphanedSessionsAsync_RemovesInactiveSessions()
    {
        // Arrange
        var manager = CreateManager();
        var session = manager.CreateSession(webSocket: null!);
        session.LastActivityAt = DateTime.UtcNow.AddHours(-1);

        // Act
        var removedCount = await manager.CleanupOrphanedSessionsAsync(TimeSpan.FromMinutes(5));

        // Assert
        removedCount.Should().Be(1);
        manager.GetSession(session.SessionId).Should().BeNull();
    }
}
