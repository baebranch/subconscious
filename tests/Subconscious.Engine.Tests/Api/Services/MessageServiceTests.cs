using FluentAssertions;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Api.Services;
using Xunit;

namespace Subconscious.Engine.Tests.Api.Services;

public class MessageServiceTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly IMessageService _service;

    public MessageServiceTests()
    {
        // A fresh fixture per test method (xUnit creates a new test class instance per
        // test) avoids cross-test state leakage from mutating tests (Update/Delete).
        _fixture = new ApiTestFixture();
        _service = new MessageService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetMessagesAsync_WithValidThread_ReturnsMessages()
    {
        // Arrange
        const string threadUuid = "test-thread-uuid";

        // Act
        var messages = await _service.GetMessagesAsync(threadUuid);

        // Assert
        messages.Should().NotBeEmpty();
        messages.Should().HaveCount(2);
        messages[0].Content.Should().Be("Hello, world!");
        messages[1].Content.Should().Be("Hello! How can I help you today?");
    }

    [Fact]
    public async Task GetMessagesAsync_WithInvalidThread_ReturnsEmptyList()
    {
        // Arrange
        const string threadUuid = "nonexistent-uuid";

        // Act
        var messages = await _service.GetMessagesAsync(threadUuid);

        // Assert
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMessageByUuidAsync_WithValidUuid_ReturnsMessage()
    {
        // Arrange
        const string uuid = "test-message-1-uuid";

        // Act
        var message = await _service.GetMessageByUuidAsync(uuid);

        // Assert
        message.Should().NotBeNull();
        message!.Uuid.Should().Be(uuid);
        message.Role.Should().Be("user");
        message.Content.Should().Be("Hello, world!");
        message.ThreadUuid.Should().Be("test-thread-uuid");
    }

    [Fact]
    public async Task GetMessageByUuidAsync_WithInvalidUuid_ReturnsNull()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";

        // Act
        var message = await _service.GetMessageByUuidAsync(uuid);

        // Assert
        message.Should().BeNull();
    }

    [Fact]
    public async Task CreateMessageAsync_CreatesNewMessage()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ThreadUuid = "test-thread-uuid",
            Role = "user",
            Content = "This is a new message"
        };

        // Act
        var created = await _service.CreateMessageAsync(request);

        // Assert
        created.Should().NotBeNull();
        created.Role.Should().Be("user");
        created.Content.Should().Be("This is a new message");
        created.ThreadUuid.Should().Be("test-thread-uuid");
        created.Uuid.Should().NotBeNullOrEmpty();

        // Verify persistence
        var retrieved = await _service.GetMessageByUuidAsync(created.Uuid);
        retrieved.Should().NotBeNull();
        retrieved!.Content.Should().Be(created.Content);

        // Verify thread was updated
        var messages = await _service.GetMessagesAsync("test-thread-uuid");
        messages.Should().HaveCount(3);
    }

    [Fact]
    public async Task CreateMessageAsync_WithInvalidThread_ThrowsException()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ThreadUuid = "nonexistent-uuid",
            Role = "user",
            Content = "Should fail"
        };

        // Act
        var act = async () => await _service.CreateMessageAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Thread*not found*");
    }

    [Fact]
    public async Task CreateMessageAsync_WithAssistantRole_Works()
    {
        // Arrange
        var request = new SendMessageRequest
        {
            ThreadUuid = "test-thread-uuid",
            Role = "assistant",
            Content = "I am an assistant response"
        };

        // Act
        var created = await _service.CreateMessageAsync(request);

        // Assert
        created.Should().NotBeNull();
        created.Role.Should().Be("assistant");
        created.Content.Should().Be("I am an assistant response");
    }

    [Fact]
    public async Task GetMessagesAsync_ReturnsMessagesInChronologicalOrder()
    {
        // Arrange
        const string threadUuid = "test-thread-uuid";

        // Act
        var messages = await _service.GetMessagesAsync(threadUuid);

        // Assert
        messages.Should().NotBeEmpty();
        for (int i = 0; i < messages.Count - 1; i++)
        {
            messages[i].CreatedAt.Should().BeBefore(messages[i + 1].CreatedAt);
        }
    }
}
