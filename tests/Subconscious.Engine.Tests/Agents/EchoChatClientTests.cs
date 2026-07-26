using FluentAssertions;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Agents;

namespace Subconscious.Engine.Tests.Agents;

public class EchoChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_EchoesLastUserMessage()
    {
        using var client = new EchoChatClient();
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a helpful assistant."),
            new(ChatRole.User, "hello there"),
        };

        var response = await client.GetResponseAsync(messages);

        response.Messages.Should().ContainSingle();
        response.Messages[0].Text.Should().Be("hello there");
        response.Messages[0].Role.Should().Be(ChatRole.Assistant);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_StreamsEachCharacter()
    {
        using var client = new EchoChatClient();
        var messages = new List<ChatMessage> { new(ChatRole.User, "hi") };

        var chunks = new List<string>();
        await foreach (var update in client.GetStreamingResponseAsync(messages))
        {
            chunks.Add(update.Text);
        }

        string.Concat(chunks).Should().Be("hi");
        chunks.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetResponseAsync_NoUserMessage_ReturnsEmptyText()
    {
        using var client = new EchoChatClient();
        var messages = new List<ChatMessage> { new(ChatRole.System, "system only") };

        var response = await client.GetResponseAsync(messages);

        response.Messages[0].Text.Should().BeEmpty();
    }
}
