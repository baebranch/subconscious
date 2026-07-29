using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Approval;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Tests.Tools;

public class BaseToolRegistryTests
{
    private static EngineContext Context() => new() { WorkspaceId = 1, ThreadId = 2 };

    [Fact]
    public void AllSlugs_ContainsTheCrossPlatformModulesRegisteredSoFar()
    {
        new BaseToolRegistry().AllSlugs().Should().Equal(["time", "calculator", "weather"]);
    }

    [Fact]
    public void GetTools_ReturnsToolsInRequestedSlugOrder()
    {
        var registry = new BaseToolRegistry();

        var tools = registry.GetTools(["calculator", "time"], Context());

        tools[0].Name.Should().Be("calculate");
        tools.Select(t => t.Name).Should().Contain("get_current_time");
    }

    [Fact]
    public void GetTools_UnknownSlug_IsSkipped()
    {
        var registry = new BaseToolRegistry();

        registry.GetTools(["time", "not-a-slug"], Context())
            .Should().HaveCount(4);
    }

    [Fact]
    public void GetToolsForConfig_DefaultConfig_ReturnsEverything()
    {
        var registry = new BaseToolRegistry();

        var tools = registry.GetToolsForConfig(ToolsConfig.Default, Context());

        tools.Select(t => t.Name).Should().BeEquivalentTo(
        [
            "get_current_time", "get_current_date", "convert_timezone", "list_common_timezones",
            "calculate", "convert_units", "list_supported_units",
            "get_current_weather", "get_weather_forecast",
        ]);
    }

    [Fact]
    public void GetToolsForConfig_BuiltinDisabled_ReturnsNothing()
    {
        var registry = new BaseToolRegistry();

        registry.GetToolsForConfig(new ToolsConfig(BuiltinEnabled: false), Context())
            .Should().BeEmpty();
    }

    [Fact]
    public void GetToolsForConfig_DisabledSlug_SkipsAllOfItsTools()
    {
        var registry = new BaseToolRegistry();
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            ["time"] = new(Enabled: false),
        });

        var tools = registry.GetToolsForConfig(config, Context());

        tools.Select(t => t.Name).Should().NotContain(["get_current_time", "get_current_date"]);
        tools.Should().HaveCount(5); // calculator (3) + weather (2)
    }

    [Fact]
    public void GetToolsForConfig_PerToolOverride_DisablesOnlyThatTool()
    {
        var registry = new BaseToolRegistry();
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            ["calculator"] = new(Tools: new Dictionary<string, bool> { ["convert_units"] = false }),
        });

        var names = registry.GetToolsForConfig(config, Context()).Select(t => t.Name).ToList();

        names.Should().Contain("calculate").And.NotContain("convert_units");
    }

    [Fact]
    public void GetToolsForConfig_UnlistedSlugsAndTools_DefaultToEnabled()
    {
        // Preserves the legacy "all tools" behaviour for partially-configured workspaces.
        var registry = new BaseToolRegistry();
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            ["calculator"] = new(),
        });

        registry.GetToolsForConfig(config, Context()).Should().HaveCount(9); // all tools
    }

    [Fact]
    public void Register_ReplacingAnExistingSlug_KeepsItsPosition()
    {
        var registry = new BaseToolRegistry();

        registry.Register(new StubToolModule("time", "get_stub"));

        registry.AllSlugs().Should().Equal(["time", "calculator", "weather"]);
        registry.GetTools(["time"], Context()).Select(t => t.Name).Should().Equal(["get_stub"]);
    }

    [Fact]
    public void Register_NewSlug_IsAppendedAndResolvable()
    {
        var registry = new BaseToolRegistry();

        registry.Register(new StubToolModule("desktop", "click_mouse"));

        registry.AllSlugs().Should().Equal(["time", "calculator", "weather", "desktop"]);
        registry.GetToolsForConfig(ToolsConfig.Default, Context())
            .Select(t => t.Name).Should().Contain("click_mouse");
    }

    [Fact]
    public void Catalog_GroupsBySlugWithOneLineDocsAndOperationKind()
    {
        var catalog = new BaseToolRegistry().Catalog();

        catalog.Keys.Should().Equal(["time", "calculator", "weather"]);
        var calculate = catalog["calculator"].Single(e => e.Name == "calculate");
        calculate.Doc.Should().NotBeNullOrWhiteSpace();
        calculate.Operation.Should().Be(OperationKind.Query);
    }

    [Fact]
    public void Catalog_ClassifiesUnknownMutatingToolAsMutation()
    {
        // A newly-registered tool with a write-ish name must default to approval-gated.
        var registry = new BaseToolRegistry();
        registry.Register(new StubToolModule("custom", "obliterate_everything"));

        registry.Catalog()["custom"].Single().Operation.Should().Be(OperationKind.Mutation);
    }

    [Theory]
    [InlineData("""{"builtin_enabled": false}""", 0)]
    [InlineData("""{"builtin": {"time": {"enabled": false}}}""", 5)] // calculator (3) + weather (2)
    [InlineData("""{"builtin": {"time": {"tools": {"get_current_date": false}}}}""", 8)] // all except get_current_date
    [InlineData("""{}""", 9)] // all tools
    public void GetToolsForConfig_FromPersistedJson_ResolvesAsPython(string json, int expectedCount)
    {
        var config = ToolsConfig.FromJson(JsonNode.Parse(json));

        new BaseToolRegistry().GetToolsForConfig(config, Context()).Should().HaveCount(expectedCount);
    }

    [Fact]
    public void ToolsConfig_FromJson_NullOrNonObject_YieldsDefault()
    {
        ToolsConfig.FromJson(null).Should().BeSameAs(ToolsConfig.Default);
        ToolsConfig.FromJson(JsonNode.Parse("[]")).Should().BeSameAs(ToolsConfig.Default);
        ToolsConfig.FromJson(JsonNode.Parse("7")).Should().BeSameAs(ToolsConfig.Default);
    }

    [Fact]
    public void ToolsConfig_FromJson_IgnoresUnrecognizedKeys()
    {
        // A config written by a newer client must not break an older engine.
        var config = ToolsConfig.FromJson(JsonNode.Parse(
            """{"builtin_enabled": true, "mcp": {"servers": []}, "builtin": {"time": {"enabled": true, "future": 1}}}"""));

        config.BuiltinEnabled.Should().BeTrue();
        config.ForSlug("time").Enabled.Should().BeTrue();
    }

    [Fact]
    public void ApprovalGate_ComposesWithTheRegistry()
    {
        // End-to-end Phase 3 wiring check: registry output feeds the HITL gate, and the
        // mutation-only policy wraps exactly the mutating tools.
        var registry = new BaseToolRegistry();
        registry.Register(new StubToolModule("custom", "delete_everything"));
        var tools = registry.GetToolsForConfig(ToolsConfig.Default, Context());

        var gated = ApprovalGate.Apply(
            tools,
            new ApprovalConfig(RequireApprovalForQueries: false, RequireApprovalForMutations: true));

        var deleteTool = gated.Single(t => t.Name == "delete_everything");
        deleteTool.Should().NotBeOfType<StubFunction>("mutating tools must be approval-wrapped");
        gated.Single(t => t.Name == "calculate").Should().BeSameAs(tools.Single(t => t.Name == "calculate"));
    }

    private sealed class StubToolModule(string slug, params string[] toolNames) : IToolModule
    {
        public string Slug => slug;

        public IReadOnlyList<AIFunction> CreateTools(EngineContext context) =>
            [.. toolNames.Select(name => (AIFunction)new StubFunction(name))];
    }

    private sealed class StubFunction(string name) : AIFunction
    {
        public override string Name => name;

        public override string Description => "Stub tool used for registry tests.";

        protected override ValueTask<object?> InvokeCoreAsync(
            AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            ValueTask.FromResult<object?>("stub");
    }
}
