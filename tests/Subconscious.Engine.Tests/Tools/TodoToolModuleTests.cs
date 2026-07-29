using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Tests.Tools;

public class TodoToolModuleTests : IDisposable
{
    private readonly SubconsciousDbContext _db;
    private readonly EngineContext _context;
    private readonly IReadOnlyList<AIFunction> _tools;

    public TodoToolModuleTests()
    {
        var options = new DbContextOptionsBuilder<SubconsciousDbContext>()
            .UseInMemoryDatabase(databaseName: $"TodoToolTests_{Guid.NewGuid()}")
            .Options;

        _db = new SubconsciousDbContext(options);
        _context = new EngineContext { WorkspaceId = 1, ThreadId = 0, Database = _db };
        _tools = new TodoToolModule().CreateTools(_context);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private AIFunction Tool(string name) => _tools.First(t => t.Name == name);

    private static async Task<JsonElement> InvokeAsync(AIFunction tool, AIFunctionArguments args)
    {
        var result = await tool.InvokeAsync(args);
        var json = JsonSerializer.Serialize(result);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void CreateTools_ReturnsExpectedTools()
    {
        _tools.Select(t => t.Name).Should().BeEquivalentTo(
            ["add_todo", "list_todos", "update_todo", "complete_todo", "delete_todo"]);
    }

    [Fact]
    public async Task AddTodo_CreatesNewTodo()
    {
        // Act
        var result = await InvokeAsync(Tool("add_todo"), new AIFunctionArguments
        {
            { "title", "Test Task" },
            { "priority", "high" }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");
        result.GetProperty("todo").GetProperty("title").GetString().Should().Be("Test Task");
        result.GetProperty("todo").GetProperty("priority").GetString().Should().Be("high");

        var todos = await _db.TodoItems.ToListAsync();
        todos.Should().HaveCount(1);
        todos[0].Title.Should().Be("Test Task");
        todos[0].Priority.Should().Be("high");
        todos[0].Status.Should().Be("open");
    }

    [Fact]
    public async Task ListTodos_ReturnsAllTodos()
    {
        // Arrange
        await SeedTodoAsync("Task 1", status: "open");
        await SeedTodoAsync("Task 2", status: "done");

        // Act
        var result = await InvokeAsync(Tool("list_todos"), new AIFunctionArguments());

        // Assert
        var titles = result.GetProperty("todos").EnumerateArray()
            .Select(t => t.GetProperty("title").GetString())
            .ToList();
        titles.Should().Contain("Task 1").And.Contain("Task 2");
    }

    [Fact]
    public async Task ListTodos_FiltersByStatus()
    {
        // Arrange
        await SeedTodoAsync("Open Task", status: "open");
        await SeedTodoAsync("Done Task", status: "done");

        // Act
        var result = await InvokeAsync(Tool("list_todos"), new AIFunctionArguments
        {
            { "status", "done" }
        });

        // Assert
        var titles = result.GetProperty("todos").EnumerateArray()
            .Select(t => t.GetProperty("title").GetString())
            .ToList();
        titles.Should().ContainSingle().Which.Should().Be("Done Task");
    }

    [Fact]
    public async Task UpdateTodo_ModifiesExistingTodo()
    {
        // Arrange
        var todo = await SeedTodoAsync("Original Title");

        // Act
        var result = await InvokeAsync(Tool("update_todo"), new AIFunctionArguments
        {
            { "todo_id", todo.Id },
            { "title", "Updated Title" },
            { "priority", "urgent" }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");
        result.GetProperty("todo").GetProperty("title").GetString().Should().Be("Updated Title");

        var updated = await _db.TodoItems.FindAsync(todo.Id);
        updated!.Title.Should().Be("Updated Title");
        updated.Priority.Should().Be("urgent");
    }

    [Fact]
    public async Task CompleteTodo_MarksTodoAsDone()
    {
        // Arrange
        var todo = await SeedTodoAsync("Task to Complete");

        // Act
        var result = await InvokeAsync(Tool("complete_todo"), new AIFunctionArguments
        {
            { "todo_id", todo.Id }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");

        var completed = await _db.TodoItems.FindAsync(todo.Id);
        completed!.Status.Should().Be("done");
    }

    [Fact]
    public async Task DeleteTodo_RemovesTodo()
    {
        // Arrange
        var todo = await SeedTodoAsync("Task to Delete");

        // Act
        var result = await InvokeAsync(Tool("delete_todo"), new AIFunctionArguments
        {
            { "todo_id", todo.Id }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");

        var deleted = await _db.TodoItems.FindAsync(todo.Id);
        deleted.Should().BeNull();
    }

    private async Task<TodoItem> SeedTodoAsync(string title, string status = "open", string priority = "normal")
    {
        var todo = new TodoItem
        {
            WorkspaceId = 1,
            Title = title,
            Status = status,
            Priority = priority,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _db.TodoItems.AddAsync(todo);
        await _db.SaveChangesAsync();
        return todo;
    }
}
