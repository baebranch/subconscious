using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Tests.Tools;

public class MemoryToolModuleTests : IDisposable
{
    private readonly SubconsciousDbContext _db;
    private readonly EngineContext _context;
    private readonly IReadOnlyList<AIFunction> _tools;

    public MemoryToolModuleTests()
    {
        var options = new DbContextOptionsBuilder<SubconsciousDbContext>()
            .UseInMemoryDatabase(databaseName: $"MemoryToolTests_{Guid.NewGuid()}")
            .Options;

        _db = new SubconsciousDbContext(options);
        _context = new EngineContext { WorkspaceId = 1, ThreadId = 0, Database = _db };
        _tools = new MemoryToolModule().CreateTools(_context);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private AIFunction Tool(string name) => _tools.First(t => t.Name == name);

    private static async Task<string?> InvokeAsync(AIFunction tool, AIFunctionArguments args)
    {
        var result = await tool.InvokeAsync(args);
        return result switch
        {
            null => null,
            string s => s,
            JsonElement e => e.GetString(),
            _ => result.ToString()
        };
    }

    [Fact]
    public void CreateTools_ReturnsExpectedTools()
    {
        _tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["remember", "recall", "list_memories", "forget", "forget_all"]);
    }

    [Fact]
    public async Task Remember_StoresNewFact()
    {
        // Act
        var result = await InvokeAsync(Tool("remember"), new AIFunctionArguments
        {
            { "key", "user_name" },
            { "value", "Alice" }
        });

        // Assert
        result.Should().Be("Remembered user_name = Alice");

        var memories = await _db.WorkspaceMemories.ToListAsync();
        memories.Should().HaveCount(1);
        memories[0].Key.Should().Be("user_name");
        memories[0].Value.Should().Be("Alice");
    }

    [Fact]
    public async Task Remember_OverwritesExistingFact()
    {
        // Arrange
        await SeedMemoryAsync("user_name", "Bob");

        // Act
        var result = await InvokeAsync(Tool("remember"), new AIFunctionArguments
        {
            { "key", "user_name" },
            { "value", "Charlie" }
        });

        // Assert
        result.Should().Be("Remembered user_name = Charlie");

        var memories = await _db.WorkspaceMemories.Where(m => m.Key == "user_name").ToListAsync();
        memories.Should().HaveCount(1);
        memories[0].Value.Should().Be("Charlie");
    }

    [Fact]
    public async Task Recall_RetrievesStoredFact()
    {
        // Arrange
        await SeedMemoryAsync("favorite_color", "blue");

        // Act
        var result = await InvokeAsync(Tool("recall"), new AIFunctionArguments
        {
            { "key", "favorite_color" }
        });

        // Assert
        result.Should().Be("blue");
    }

    [Fact]
    public async Task Recall_ReturnsNotFoundForMissingKey()
    {
        // Act
        var result = await InvokeAsync(Tool("recall"), new AIFunctionArguments
        {
            { "key", "nonexistent_key" }
        });

        // Assert
        result.Should().Contain("No memory found for key 'nonexistent_key'");
    }

    [Fact]
    public async Task ListMemories_ReturnsAllFacts()
    {
        // Arrange
        await SeedMemoryAsync("key1", "value1");
        await SeedMemoryAsync("key2", "value2");

        // Act
        var result = await InvokeAsync(Tool("list_memories"), new AIFunctionArguments());

        // Assert
        result.Should().Contain("key1 = value1");
        result.Should().Contain("key2 = value2");
    }

    [Fact]
    public async Task Forget_RemovesMemory()
    {
        // Arrange
        await SeedMemoryAsync("temp_key", "temp_value");

        // Act
        var result = await InvokeAsync(Tool("forget"), new AIFunctionArguments
        {
            { "key", "temp_key" }
        });

        // Assert
        result.Should().Be("Forgot temp_key");

        var memories = await _db.WorkspaceMemories.Where(m => m.Key == "temp_key").ToListAsync();
        memories.Should().BeEmpty();
    }

    [Fact]
    public async Task ForgetAll_ClearsAllMemories()
    {
        // Arrange
        await SeedMemoryAsync("key1", "value1");
        await SeedMemoryAsync("key2", "value2");

        // Act
        var result = await InvokeAsync(Tool("forget_all"), new AIFunctionArguments());

        // Assert
        result.Should().Contain("Cleared 2 memories");

        var memories = await _db.WorkspaceMemories.ToListAsync();
        memories.Should().BeEmpty();
    }

    private async Task<WorkspaceMemory> SeedMemoryAsync(string key, string value)
    {
        var memory = new WorkspaceMemory
        {
            WorkspaceId = 1,
            Key = key,
            Value = value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _db.WorkspaceMemories.AddAsync(memory);
        await _db.SaveChangesAsync();
        return memory;
    }
}
