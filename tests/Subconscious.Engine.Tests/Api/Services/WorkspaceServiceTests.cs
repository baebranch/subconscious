using FluentAssertions;
using Subconscious.Engine.Api.DTOs;
using Subconscious.Engine.Api.Services;
using Xunit;

namespace Subconscious.Engine.Tests.Api.Services;

public class WorkspaceServiceTests : IDisposable
{
    private readonly ApiTestFixture _fixture;
    private readonly IWorkspaceService _service;

    public WorkspaceServiceTests()
    {
        // A fresh fixture per test method (xUnit creates a new test class instance per
        // test) avoids cross-test state leakage from mutating tests (Update/Delete).
        _fixture = new ApiTestFixture();
        _service = new WorkspaceService(_fixture.Context);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task GetAllWorkspacesAsync_ReturnsAllWorkspaces()
    {
        // Act
        var workspaces = await _service.GetAllWorkspacesAsync();

        // Assert
        workspaces.Should().NotBeEmpty();
        workspaces.Should().ContainSingle(w => w.Name == "Test Workspace");
    }

    [Fact]
    public async Task GetWorkspaceByUuidAsync_WithValidUuid_ReturnsWorkspace()
    {
        // Arrange
        const string uuid = "test-workspace-uuid";

        // Act
        var workspace = await _service.GetWorkspaceByUuidAsync(uuid);

        // Assert
        workspace.Should().NotBeNull();
        workspace!.Uuid.Should().Be(uuid);
        workspace.Name.Should().Be("Test Workspace");
    }

    [Fact]
    public async Task GetWorkspaceByUuidAsync_WithInvalidUuid_ReturnsNull()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";

        // Act
        var workspace = await _service.GetWorkspaceByUuidAsync(uuid);

        // Assert
        workspace.Should().BeNull();
    }

    [Fact]
    public async Task CreateWorkspaceAsync_CreatesNewWorkspace()
    {
        // Arrange
        var request = new CreateWorkspaceRequest
        {
            Name = "New Workspace",
            Description = "A new test workspace",
            DefaultModelId = "gpt-3.5-turbo"
        };

        // Act
        var created = await _service.CreateWorkspaceAsync(request);

        // Assert
        created.Should().NotBeNull();
        created.Name.Should().Be("New Workspace");
        created.Description.Should().Be("A new test workspace");
        created.DefaultModelId.Should().Be("gpt-3.5-turbo");
        created.Uuid.Should().NotBeNullOrEmpty();

        // Verify it was persisted
        var retrieved = await _service.GetWorkspaceByUuidAsync(created.Uuid);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be(created.Name);
    }

    [Fact]
    public async Task UpdateWorkspaceAsync_WithValidUuid_UpdatesWorkspace()
    {
        // Arrange
        const string uuid = "test-workspace-uuid";
        var request = new CreateWorkspaceRequest
        {
            Name = "Updated Workspace",
            Description = "Updated description",
            DefaultModelId = "claude-3"
        };

        // Act
        var updated = await _service.UpdateWorkspaceAsync(uuid, request);

        // Assert
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("Updated Workspace");
        updated.Description.Should().Be("Updated description");
        updated.DefaultModelId.Should().Be("claude-3");

        // Verify persistence
        var retrieved = await _service.GetWorkspaceByUuidAsync(uuid);
        retrieved!.Name.Should().Be("Updated Workspace");
    }

    [Fact]
    public async Task UpdateWorkspaceAsync_WithInvalidUuid_ReturnsNull()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";
        var request = new CreateWorkspaceRequest
        {
            Name = "Should not work",
            Description = "Should not work"
        };

        // Act
        var updated = await _service.UpdateWorkspaceAsync(uuid, request);

        // Assert
        updated.Should().BeNull();
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_WithValidUuid_DeletesWorkspace()
    {
        // Arrange - create a workspace to delete
        var createRequest = new CreateWorkspaceRequest
        {
            Name = "To Be Deleted",
            Description = "This will be deleted"
        };
        var created = await _service.CreateWorkspaceAsync(createRequest);

        // Act
        var deleted = await _service.DeleteWorkspaceAsync(created.Uuid);

        // Assert
        deleted.Should().BeTrue();

        // Verify it's gone
        var retrieved = await _service.GetWorkspaceByUuidAsync(created.Uuid);
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task DeleteWorkspaceAsync_WithInvalidUuid_ReturnsFalse()
    {
        // Arrange
        const string uuid = "nonexistent-uuid";

        // Act
        var deleted = await _service.DeleteWorkspaceAsync(uuid);

        // Assert
        deleted.Should().BeFalse();
    }
}
