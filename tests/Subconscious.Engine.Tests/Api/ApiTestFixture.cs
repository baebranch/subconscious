using Microsoft.EntityFrameworkCore;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Xunit;
using ThreadEntity = Subconscious.Engine.Data.Entities.Thread;

namespace Subconscious.Engine.Tests.Api;

/// <summary>
/// Test fixture that provides an in-memory database for API tests.
/// </summary>
public class ApiTestFixture : IDisposable
{
    public SubconsciousDbContext Context { get; private set; }

    public ApiTestFixture()
    {
        var options = new DbContextOptionsBuilder<SubconsciousDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        Context = new SubconsciousDbContext(options);

        // Seed test data
        SeedTestData();
    }

    private void SeedTestData()
    {
        // Create test network
        var network = new Network
        {
            Id = 1,
            Uuid = "test-network-uuid",
            Name = "Test Network",
            Description = "Test network for API tests",
            CreatedAt = DateTime.UtcNow
        };

        Context.Networks.Add(network);

        // Create test workspace
        var workspace = new Workspace
        {
            Id = 1,
            Uuid = "test-workspace-uuid",
            Name = "Test Workspace",
            Description = "Test workspace",
            NetworkId = network.Id,
            DefaultModelId = "gpt-4",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Context.Workspaces.Add(workspace);

        // Create test thread
        var thread = new ThreadEntity
        {
            Id = 1,
            Uuid = "test-thread-uuid",
            WorkspaceId = workspace.Id,
            Title = "Test Thread",
            Description = "Test conversation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        Context.Threads.Add(thread);

        // Create test messages
        var messages = new[]
        {
            new Message
            {
                Id = 1,
                Uuid = "test-message-1-uuid",
                ThreadId = thread.Id,
                Role = "user",
                Content = "Hello, world!",
                CreatedAt = DateTime.UtcNow.AddMinutes(-5)
            },
            new Message
            {
                Id = 2,
                Uuid = "test-message-2-uuid",
                ThreadId = thread.Id,
                Role = "assistant",
                Content = "Hello! How can I help you today?",
                CreatedAt = DateTime.UtcNow.AddMinutes(-4)
            }
        };

        Context.Messages.AddRange(messages);

        Context.SaveChanges();
    }

    public void Dispose()
    {
        Context.Dispose();
    }
}


