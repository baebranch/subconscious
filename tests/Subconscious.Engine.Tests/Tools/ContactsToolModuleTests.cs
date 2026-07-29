using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Data;
using Subconscious.Engine.Data.Entities;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Tests.Tools;

public class ContactsToolModuleTests : IDisposable
{
    private readonly SubconsciousDbContext _db;
    private readonly EngineContext _context;
    private readonly IReadOnlyList<AIFunction> _tools;

    public ContactsToolModuleTests()
    {
        var options = new DbContextOptionsBuilder<SubconsciousDbContext>()
            .UseInMemoryDatabase(databaseName: $"ContactsToolTests_{Guid.NewGuid()}")
            .Options;

        _db = new SubconsciousDbContext(options);
        _context = new EngineContext { WorkspaceId = 1, ThreadId = 0, Database = _db };
        _tools = new ContactsToolModule().CreateTools(_context);
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
            ["add_contact", "list_contacts", "find_contact", "update_contact", "delete_contact"]);
    }

    [Fact]
    public async Task AddContact_CreatesNewContact()
    {
        // Act
        var result = await InvokeAsync(Tool("add_contact"), new AIFunctionArguments
        {
            { "name", "John Doe" },
            { "email", "john@example.com" },
            { "phone", "555-1234" }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");
        result.GetProperty("contact").GetProperty("name").GetString().Should().Be("John Doe");

        var contacts = await _db.Contacts.ToListAsync();
        contacts.Should().HaveCount(1);
        contacts[0].Name.Should().Be("John Doe");
        contacts[0].Email.Should().Be("john@example.com");
        contacts[0].Phone.Should().Be("555-1234");
    }

    [Fact]
    public async Task ListContacts_ReturnsAllContacts()
    {
        // Arrange
        await SeedContactAsync("Alice", email: "alice@example.com");
        await SeedContactAsync("Bob", phone: "555-5555");

        // Act
        var result = await InvokeAsync(Tool("list_contacts"), new AIFunctionArguments());

        // Assert
        var names = result.GetProperty("contacts").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("Alice").And.Contain("Bob");
    }

    [Fact]
    public async Task FindContact_SearchesByName()
    {
        // Arrange
        await SeedContactAsync("Alice Smith", email: "alice@example.com");
        await SeedContactAsync("Bob Jones", email: "bob@example.com");

        // Act
        var result = await InvokeAsync(Tool("find_contact"), new AIFunctionArguments
        {
            { "query", "Alice" }
        });

        // Assert
        var names = result.GetProperty("contacts").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        names.Should().ContainSingle().Which.Should().Be("Alice Smith");
    }

    [Fact]
    public async Task FindContact_SearchesByEmail()
    {
        // Arrange
        await SeedContactAsync("Charlie", email: "charlie@special.com");

        // Act
        var result = await InvokeAsync(Tool("find_contact"), new AIFunctionArguments
        {
            { "query", "special.com" }
        });

        // Assert
        var names = result.GetProperty("contacts").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToList();
        names.Should().Contain("Charlie");
    }

    [Fact]
    public async Task UpdateContact_ModifiesExistingContact()
    {
        // Arrange
        var contact = await SeedContactAsync("Original Name", email: "old@example.com");

        // Act
        var result = await InvokeAsync(Tool("update_contact"), new AIFunctionArguments
        {
            { "contact_id", contact.Id },
            { "name", "Updated Name" },
            { "email", "new@example.com" }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");

        var updated = await _db.Contacts.FindAsync(contact.Id);
        updated!.Name.Should().Be("Updated Name");
        updated.Email.Should().Be("new@example.com");
    }

    [Fact]
    public async Task DeleteContact_RemovesContact()
    {
        // Arrange
        var contact = await SeedContactAsync("To Delete", email: "delete@example.com");

        // Act
        var result = await InvokeAsync(Tool("delete_contact"), new AIFunctionArguments
        {
            { "contact_id", contact.Id }
        });

        // Assert
        result.GetProperty("status").GetString().Should().Be("success");

        var deleted = await _db.Contacts.FindAsync(contact.Id);
        deleted.Should().BeNull();
    }

    private async Task<Contact> SeedContactAsync(string name, string? email = null, string? phone = null)
    {
        var contact = new Contact
        {
            WorkspaceId = 1,
            Name = name,
            Email = email,
            Phone = phone,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _db.Contacts.AddAsync(contact);
        await _db.SaveChangesAsync();
        return contact;
    }
}
