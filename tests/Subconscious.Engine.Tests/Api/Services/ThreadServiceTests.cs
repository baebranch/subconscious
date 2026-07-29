using FluentAssertions;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Api.Services;
using Xunit;

namespace Subconscious.Engine.Tests.Api.Services;

public class ThreadServiceTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly IThreadService _service;

    public ThreadServiceTests()
    {
        // A fresh fixture per test method (xUnit creates a new test class instance per
        // test) avoids cross-test state leakage from mutating tests (Update/Delete).
        _fixture = new ApiTestFixture();
        _service = new ThreadService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetThreadsAsync_WithValidWorkspace_ReturnsThreads()
    {
        // Arrange
        const string workspaceUuid = "test-workspace-uuid";

        // Act
        var threads = await _service.GetThreadsAsync(workspaceUuid);

        // Assert
        threads.Should().NotBeEmpty();
        threads.Should().ContainSingle(t => t.Title == "Test Thread");
    }

    [Fact]
    public async Task GetThreadsAsync_WithInvalidWorkspace_ReturnsEmptyList()
    {
        // Arrange
        const string workspaceUuid = "nonexistent-uuid";

        // Act
        var threads = await _service.GetThreadsAsync(workspaceUuid);

        // Assert
        threads.Should().BeEmpty();
    }

    [Fact]
    public async Task GetThreadByUuidAsync_WithValidUuid_ReturnsThread()
    {
        // Arrange
        const string uuid = "test-thread-uuid";

        // Act
        var thread = await _service.GetThreadByUuidAsync(uuid);

        // Assert
        thread.Should().NotBeNull();
        thread!.Uuid.Should().Be(uuid);
        thread.Title.Should().Be("Test Thread");
        thread.WorkspaceUuid.Should().Be("test-workspace-uuid");
    }

    [Fact]
    public async Task GetThreadByUuidAsync_WithInvalidUuid_ReturnsNull()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";

        // Act
        var thread = await _service.GetThreadByUuidAsync(uuid);

        // Assert
        thread.Should().BeNull();
    }

    [Fact]
    public async Task CreateThreadAsync_CreatesNewThread()
    {
        // Arrange
        var request = new CreateThreadRequest
        {
            WorkspaceUuid = "test-workspace-uuid",
            Title = "New Thread",
            Description = "A new conversation",
            DefaultModelId = "gpt-4"
        };

        // Act
        var created = await _service.CreateThreadAsync(request);

        // Assert
        created.Should().NotBeNull();
        created.Title.Should().Be("New Thread");
        created.Description.Should().Be("A new conversation");
        created.DefaultModelId.Should().Be("gpt-4");
        created.WorkspaceUuid.Should().Be("test-workspace-uuid");
        created.Uuid.Should().NotBeNullOrEmpty();

        // Verify persistence
        var retrieved = await _service.GetThreadByUuidAsync(created.Uuid);
        retrieved.Should().NotBeNull();
        retrieved!.Title.Should().Be(created.Title);
    }

    [Fact]
    public async Task CreateThreadAsync_WithInvalidWorkspace_ThrowsException()
    {
        // Arrange
        var request = new CreateThreadRequest
        {
            WorkspaceUuid = "nonexistent-uuid",
            Title = "Should fail"
        };

        // Act
        var act = async () => await _service.CreateThreadAsync(request);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Workspace*not found*");
    }

    [Fact]
    public async Task UpdateThreadAsync_WithValidUuid_UpdatesThread()
    {
        // Arrange
        const string uuid = "test-thread-uuid";
        var request = new UpdateThreadRequest
        {
            Title = "Updated Thread",
            Description = "Updated description",
            DefaultModelId = "claude-3"
        };

        // Act
        var updated = await _service.UpdateThreadAsync(uuid, request);

        // Assert
        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Updated Thread");
        updated.Description.Should().Be("Updated description");
        updated.DefaultModelId.Should().Be("claude-3");

        // Verify persistence
        var retrieved = await _service.GetThreadByUuidAsync(uuid);
        retrieved!.Title.Should().Be("Updated Thread");
    }

    [Fact]
    public async Task UpdateThreadAsync_WithInvalidUuid_ReturnsNull()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";
        var request = new UpdateThreadRequest
        {
            Title = "Should not work"
        };

        // Act
        var updated = await _service.UpdateThreadAsync(uuid, request);

        // Assert
        updated.Should().BeNull();
    }

    [Fact]
    public async Task DeleteThreadAsync_WithValidUuid_DeletesThread()
    {
        // Arrange - create a thread to delete
        var createRequest = new CreateThreadRequest
        {
            WorkspaceUuid = "test-workspace-uuid",
            Title = "To Be Deleted"
        };
        var created = await _service.CreateThreadAsync(createRequest);

        // Act
        var deleted = await _service.DeleteThreadAsync(created.Uuid);

        // Assert
        deleted.Should().BeTrue();

        // Verify it's gone
        var retrieved = await _service.GetThreadByUuidAsync(created.Uuid);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteThreadAsync_WithInvalidUuid_ReturnsFalse()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";

        // Act
        var deleted = await _service.DeleteThreadAsync(uuid);

        // Assert
        deleted.Should().BeFalse();
    }
}
