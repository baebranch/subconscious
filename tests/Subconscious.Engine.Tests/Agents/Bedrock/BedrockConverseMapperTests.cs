using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Agents.Bedrock;

namespace Subconscious.Engine.Tests.Agents.Bedrock;

public class BedrockConverseMapperTests
{
    [Fact]
    public void BuildRequest_HoistsSystemMessagesToTopLevelSystemArray()
    {
        // Bedrock has no system role: the prompt belongs in a top-level "system" array.
        var request = BedrockConverseMapper.BuildRequest(
            [
                new ChatMessage(ChatRole.System, "You are helpful."),
                new ChatMessage(ChatRole.User, "Hi"),
            ],
            options: null);

        request["system"]!.AsArray().Should().HaveCount(1);
        request["system"]![0]!["text"]!.GetValue<string>().Should().Be("You are helpful.");
        request["messages"]!.AsArray().Should().HaveCount(1);
        request["messages"]![0]!["role"]!.GetValue<string>().Should().Be("user");
    }

    [Fact]
    public void BuildRequest_NoSystemMessage_OmitsSystemField()
    {
        var request = BedrockConverseMapper.BuildRequest([new ChatMessage(ChatRole.User, "Hi")], null);

        request.ContainsKey("system").Should().BeFalse();
    }

    [Fact]
    public void BuildRequest_MapsAssistantRoleAndWrapsContentInTextBlocks()
    {
        var request = BedrockConverseMapper.BuildRequest(
            [
                new ChatMessage(ChatRole.User, "Hi"),
                new ChatMessage(ChatRole.Assistant, "Hello"),
            ],
            options: null);

        var messages = request["messages"]!.AsArray();
        messages[0]!["role"]!.GetValue<string>().Should().Be("user");
        messages[1]!["role"]!.GetValue<string>().Should().Be("assistant");
        messages[1]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("Hello");
    }

    [Fact]
    public void BuildRequest_NonUserAssistantRole_IsAttributedToUserRatherThanDropped()
    {
        // Losing tool output silently would be worse than attributing it to the user turn.
        var request = BedrockConverseMapper.BuildRequest(
            [new ChatMessage(ChatRole.Tool, "tool output")],
            options: null);

        var messages = request["messages"]!.AsArray();
        messages.Should().HaveCount(1);
        messages[0]!["role"]!.GetValue<string>().Should().Be("user");
        messages[0]!["content"]![0]!["text"]!.GetValue<string>().Should().Be("tool output");
    }

    [Fact]
    public void BuildRequest_MapsInferenceConfigFromChatOptions()
    {
        var options = new ChatOptions
        {
            MaxOutputTokens = 512,
            Temperature = 0.3f,
            TopP = 0.9f,
            StopSequences = ["STOP"],
        };

        var config = BedrockConverseMapper.BuildRequest([new ChatMessage(ChatRole.User, "Hi")], options)
            ["inferenceConfig"]!.AsObject();

        config["maxTokens"]!.GetValue<int>().Should().Be(512);
        config["temperature"]!.GetValue<float>().Should().BeApproximately(0.3f, 1e-6f);
        config["topP"]!.GetValue<float>().Should().BeApproximately(0.9f, 1e-6f);
        config["stopSequences"]!.AsArray()[0]!.GetValue<string>().Should().Be("STOP");
    }

    [Fact]
    public void BuildRequest_EmptyChatOptions_OmitsInferenceConfig()
    {
        var request = BedrockConverseMapper.BuildRequest(
            [new ChatMessage(ChatRole.User, "Hi")], new ChatOptions());

        request.ContainsKey("inferenceConfig").Should().BeFalse();
    }

    [Fact]
    public void ExtractResponseText_ConcatenatesAllTextBlocks()
    {
        const string json = """
        {"output":{"message":{"role":"assistant","content":[{"text":"Hello "},{"text":"world"}]}}}
        """;

        BedrockConverseMapper.ExtractResponseText(json).Should().Be("Hello world");
    }

    [Fact]
    public void ExtractResponseText_IgnoresNonTextBlocks()
    {
        const string json = """
        {"output":{"message":{"content":[{"toolUse":{"name":"x"}},{"text":"only this"}]}}}
        """;

        BedrockConverseMapper.ExtractResponseText(json).Should().Be("only this");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"output":{}}""")]
    [InlineData("""{"output":{"message":{}}}""")]
    [InlineData("""{"output":{"message":{"content":{}}}}""")]
    public void ExtractResponseText_MissingOrMalformedShape_ReturnsEmpty(string json)
    {
        BedrockConverseMapper.ExtractResponseText(json).Should().BeEmpty();
    }

    [Fact]
    public void ExtractStopReason_ReadsTopLevelStopReason()
    {
        BedrockConverseMapper.ExtractStopReason("""{"stopReason":"end_turn"}""").Should().Be("end_turn");
    }

    [Fact]
    public void ExtractStopReason_Absent_ReturnsNull()
    {
        BedrockConverseMapper.ExtractStopReason("""{}""").Should().BeNull();
    }

    [Fact]
    public void ExtractDeltaText_ReadsContentBlockDeltaText()
    {
        const string payload = """{"contentBlockIndex":0,"delta":{"text":"chunk"}}""";

        BedrockConverseMapper.ExtractDeltaText(payload).Should().Be("chunk");
    }

    [Theory]
    [InlineData("""{"contentBlockIndex":0,"delta":{"toolUse":{"input":"{}"}}}""")]
    [InlineData("""{"contentBlockIndex":0}""")]
    public void ExtractDeltaText_NonTextDelta_ReturnsNull(string payload)
    {
        BedrockConverseMapper.ExtractDeltaText(payload).Should().BeNull();
    }

    [Fact]
    public void ExtractUsage_ReadsAllTokenCounts()
    {
        const string json = """{"usage":{"inputTokens":11,"outputTokens":22,"totalTokens":33}}""";

        var usage = BedrockConverseMapper.ExtractUsage(json);

        usage.Should().NotBeNull();
        usage!.InputTokenCount.Should().Be(11);
        usage.OutputTokenCount.Should().Be(22);
        usage.TotalTokenCount.Should().Be(33);
    }

    [Fact]
    public void ExtractUsage_NoUsageBlock_ReturnsNull()
    {
        BedrockConverseMapper.ExtractUsage("""{"stopReason":"end_turn"}""").Should().BeNull();
    }

    [Fact]
    public void ExtractUsage_PartialUsageBlock_ReturnsWhatIsPresent()
    {
        var usage = BedrockConverseMapper.ExtractUsage("""{"usage":{"inputTokens":5}}""");

        usage.Should().NotBeNull();
        usage!.InputTokenCount.Should().Be(5);
        usage.OutputTokenCount.Should().BeNull();
    }

    [Fact]
    public void BuildRequest_ProducesParseableJson()
    {
        // The client serializes this object straight onto the wire, so it must round-trip.
        var request = BedrockConverseMapper.BuildRequest(
            [new ChatMessage(ChatRole.System, "sys"), new ChatMessage(ChatRole.User, "hi")],
            new ChatOptions { MaxOutputTokens = 10 });

        var reparsed = JsonNode.Parse(request.ToJsonString());

        reparsed!["messages"]!.AsArray().Should().HaveCount(1);
    }
}
