using FluentAssertions;
using LlmTornado.Code;
using Subconscious.Engine.Agents;

namespace Subconscious.Engine.Tests.Agents;

public class ProviderCatalogTests
{
    [Theory]
    [InlineData("openai", LLmProviders.OpenAi)]
    [InlineData("OpenAI", LLmProviders.OpenAi)]
    [InlineData("anthropic", LLmProviders.Anthropic)]
    [InlineData("gemini", LLmProviders.Google)]
    [InlineData("groq", LLmProviders.Groq)]
    [InlineData("mistral", LLmProviders.Mistral)]
    [InlineData("xai", LLmProviders.XAi)]
    public void Resolve_DirectProviders_ReturnsExpectedEnum(string providerName, LLmProviders expected)
    {
        ProviderCatalog.Resolve(providerName).Should().Be(expected);
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("lm studio")]
    [InlineData("custom")]
    [InlineData("together ai")]
    public void Resolve_OpenAiCompatibleProviders_ReturnsCustom(string providerName)
    {
        ProviderCatalog.Resolve(providerName).Should().Be(LLmProviders.Custom);
    }

    [Fact]
    public void Resolve_Bedrock_ThrowsBecauseItIsSelfImplemented()
    {
        // Bedrock is supported, but not via a Tornado connector — callers must route it through
        // IsSelfImplemented() instead of Resolve().
        var act = () => ProviderCatalog.Resolve("bedrock");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsSelfImplemented_Bedrock_ReturnsTrue()
    {
        ProviderCatalog.IsSelfImplemented("bedrock").Should().BeTrue();
        ProviderCatalog.IsSelfImplemented("Bedrock").Should().BeTrue();
    }

    [Fact]
    public void IsSelfImplemented_TornadoBackedProvider_ReturnsFalse()
    {
        ProviderCatalog.IsSelfImplemented("openai").Should().BeFalse();
        ProviderCatalog.IsSelfImplemented("ollama").Should().BeFalse();
    }

    [Fact]
    public void Resolve_HuggingFace_ThrowsNotSupported()
    {
        var act = () => ProviderCatalog.Resolve("hugging face");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void Resolve_UnknownProvider_ThrowsArgumentException()
    {
        var act = () => ProviderCatalog.Resolve("totally-unknown-provider");
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("ollama")]
    [InlineData("lm studio")]
    [InlineData("custom")]
    public void RequiresNoApiKey_LocalProviders_ReturnsTrue(string providerName)
    {
        ProviderCatalog.RequiresNoApiKey(providerName).Should().BeTrue();
    }

    [Fact]
    public void RequiresNoApiKey_OpenAi_ReturnsFalse()
    {
        ProviderCatalog.RequiresNoApiKey("openai").Should().BeFalse();
    }

    [Theory]
    [InlineData("ollama", "http://localhost:11434/v1")]
    [InlineData("lm studio", "http://127.0.0.1:1234/v1")]
    public void DefaultEndpoint_KnownLocalProviders_ReturnsExpected(string providerName, string expected)
    {
        ProviderCatalog.DefaultEndpoint(providerName).Should().Be(expected);
    }

    [Fact]
    public void DefaultEndpoint_UnknownProvider_ReturnsNull()
    {
        ProviderCatalog.DefaultEndpoint("openai").Should().BeNull();
    }

    [Fact]
    public void IsSupported_Bedrock_ReturnsTrue()
    {
        // Bedrock is supported via Subconscious's own Converse-API provider.
        ProviderCatalog.IsSupported("bedrock").Should().BeTrue();
    }

    [Fact]
    public void IsSupported_HuggingFace_ReturnsFalse()
    {
        ProviderCatalog.IsSupported("hugging face").Should().BeFalse();
    }

    [Fact]
    public void IsSupported_OpenAi_ReturnsTrue()
    {
        ProviderCatalog.IsSupported("openai").Should().BeTrue();
    }
}
