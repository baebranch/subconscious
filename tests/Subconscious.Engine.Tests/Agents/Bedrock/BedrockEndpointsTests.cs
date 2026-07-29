using FluentAssertions;
using Subconscious.Engine.Agents;
using Subconscious.Engine.Agents.Bedrock;

namespace Subconscious.Engine.Tests.Agents.Bedrock;

public class BedrockEndpointsTests
{
    private static ModelConfig Config(
        string model = "anthropic.claude-v2",
        string? region = null,
        string? baseUrl = null) =>
        new(Id: "bedrock-1", Provider: "bedrock", Model: model, Region: region, BaseUrl: baseUrl);

    [Fact]
    public void ResolveRegion_ExplicitRegion_Wins()
    {
        BedrockEndpoints.ResolveRegion(Config(region: "eu-west-2", baseUrl: "us-east-1"))
            .Should().Be("eu-west-2");
    }

    [Fact]
    public void ResolveRegion_FallsBackToBaseUrl()
    {
        // agent.py allowed the region to be stored in base_url; that dual use is preserved.
        BedrockEndpoints.ResolveRegion(Config(baseUrl: "ap-southeast-2")).Should().Be("ap-southeast-2");
    }

    [Fact]
    public void ResolveRegion_ReadsRegionFromModelArn()
    {
        var arn = "arn:aws:bedrock:eu-central-1:123456789012:inference-profile/eu.anthropic.claude-v2";

        BedrockEndpoints.ResolveRegion(Config(model: arn)).Should().Be("eu-central-1");
    }

    [Fact]
    public void ResolveRegion_ExplicitRegionBeatsArnRegion()
    {
        var arn = "arn:aws:bedrock:eu-central-1:123456789012:inference-profile/eu.anthropic.claude-v2";

        BedrockEndpoints.ResolveRegion(Config(model: arn, region: "us-west-2")).Should().Be("us-west-2");
    }

    [Fact]
    public void ResolveRegion_MalformedArn_DoesNotThrow()
    {
        BedrockEndpoints.ResolveRegion(Config(model: "arn:aws:bedrock:"))
            .Should().NotBe(string.Empty);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void ResolveRegion_BlankValuesAreIgnored(string blank)
    {
        // A whitespace-only region must not be mistaken for a configured one.
        var config = Config(region: blank, baseUrl: "us-east-2");

        BedrockEndpoints.ResolveRegion(config).Should().Be("us-east-2");
    }

    [Fact]
    public void ServiceEndpoint_UsesRegionalBedrockRuntimeHost()
    {
        BedrockEndpoints.ServiceEndpoint("us-east-1")
            .Should().Be("https://bedrock-runtime.us-east-1.amazonaws.com");
    }

    [Fact]
    public void ConverseUrl_NonStreaming_UsesConverseAction()
    {
        BedrockEndpoints.ConverseUrl("us-east-1", "anthropic.claude-v2", streaming: false)
            .Should().Be("https://bedrock-runtime.us-east-1.amazonaws.com/model/anthropic.claude-v2/converse");
    }

    [Fact]
    public void ConverseUrl_Streaming_UsesConverseStreamAction()
    {
        BedrockEndpoints.ConverseUrl("us-east-1", "anthropic.claude-v2", streaming: true)
            .Should().EndWith("/converse-stream");
    }

    [Fact]
    public void ConverseUrl_EscapesArnModelIds()
    {
        // An inference-profile ARN contains ':' and '/', which must not be read as path segments.
        var arn = "arn:aws:bedrock:us-east-1:123456789012:inference-profile/us.anthropic.claude-v2";

        var url = BedrockEndpoints.ConverseUrl("us-east-1", arn, streaming: false);

        url.Should().Contain("%3A").And.Contain("%2F");
        url.Should().Be(
            "https://bedrock-runtime.us-east-1.amazonaws.com/model/"
            + Uri.EscapeDataString(arn) + "/converse");
    }
}
