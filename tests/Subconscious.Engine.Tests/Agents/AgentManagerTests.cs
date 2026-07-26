using FluentAssertions;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Agents;

namespace Subconscious.Engine.Tests.Agents;

public class AgentManagerTests
{
    [Fact]
    public void BuildChatClient_EchoConfig_ReturnsEchoChatClient()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "echo-1", Provider: "subconscious", Model: "echo");

        using var client = manager.BuildChatClient(config);

        client.Should().BeOfType<EchoChatClient>();
    }

    [Fact]
    public async Task BuildChatClient_EchoConfig_ProducesUsableClient()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "echo-1", Provider: "subconscious", Model: "echo");
        using var client = manager.BuildChatClient(config);

        var response = await client.GetResponseAsync([new ChatMessage(ChatRole.User, "ping")]);

        response.Messages[0].Text.Should().Be("ping");
    }

    [Fact]
    public void BuildChatClient_OpenAiConfig_ConstructsClientWithoutNetworkCall()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "openai-1", Provider: "openai", Model: "gpt-4o", ApiKey: "test-key");

        using var client = manager.BuildChatClient(config);

        client.Should().NotBeNull();
    }

    [Fact]
    public void BuildChatClient_OllamaConfig_UsesDefaultLocalEndpoint()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "ollama-1", Provider: "ollama", Model: "llama3");

        // Constructing the client should succeed even with no explicit base_url — it falls
        // back to ProviderCatalog.DefaultEndpoint, mirroring agent.py's custom_endpoints().
        using var client = manager.BuildChatClient(config);

        client.Should().NotBeNull();
    }

    [Fact]
    public void BuildChatClient_BedrockConfig_ThrowsNotSupported()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "bedrock-1", Provider: "bedrock", Model: "anthropic.claude-v2");

        var act = () => manager.BuildChatClient(config);

        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void BuildChatClient_EmptyModelName_Throws()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "bad-1", Provider: "openai", Model: "   ", ApiKey: "test-key");

        var act = () => manager.BuildChatClient(config);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildChatClient_OpenAiCompatibleWithNoBaseUrlOrDefault_Throws()
    {
        var manager = new AgentManager();
        var config = new ModelConfig(Id: "custom-1", Provider: "custom", Model: "some-model");

        var act = () => manager.BuildChatClient(config);

        act.Should().Throw<InvalidOperationException>();
    }
}
