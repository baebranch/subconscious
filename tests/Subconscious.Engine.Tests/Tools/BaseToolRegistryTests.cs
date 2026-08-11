using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Subconscious.Engine.Approval;
using Subconscious.Engine.Tools;

namespace Subconscious.Engine.Tests.Tools;

public class BaseToolRegistryTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedTools =
        new Dictionary<string, string[]>
        {
            ["time"] = ["get_current_time", "get_current_date", "convert_timezone", "list_common_timezones"],
            ["calculator"] = ["calculate", "convert_units", "list_supported_units"],
            ["weather"] = ["get_current_weather", "get_weather_forecast"],
            ["todo"] = ["add_todo", "list_todos", "update_todo", "complete_todo", "delete_todo"],
            ["memory"] = ["remember", "recall", "list_memories", "forget", "forget_all"],
            ["notes"] = ["save_note", "list_notes", "get_note", "delete_note"],
            ["contacts"] = ["add_contact", "list_contacts", "find_contact", "update_contact", "delete_contact"],
            ["knowledge"] = ["search_knowledge", "search_knowledge_graph"],
        };

    private static readonly string[] ExpectedSlugs = [.. ExpectedTools.Keys];
    private static readonly string[] AllToolNames = [.. ExpectedTools.Values.SelectMany(names => names)];

    private static EngineContext Context() => new() { WorkspaceId = 1, ThreadId = 2 };

    [Fact]
    public void AllSlugs_ContainsTheCrossPlatformCatalogInRegistrationOrder()
    {
        new BaseToolRegistry().AllSlugs().Should().Equal(ExpectedSlugs);
    }

    [Fact]
    public void GetTools_ReturnsToolsInRequestedSlugOrder()
    {
        var registry = new BaseToolRegistry();

        var tools = registry.GetTools(["calculator", "time"], Context());

        tools.Select(t => t.Name).Should().Equal(
            ExpectedTools["calculator"].Concat(ExpectedTools["time"]));
    }

    [Fact]
    public void GetTools_UnknownSlug_IsSkipped()
    {
        var registry = new BaseToolRegistry();

        registry.GetTools(["time", "not-a-slug"], Context()).Select(t => t.Name)
            .Should().Equal(ExpectedTools["time"]);
    }

    [Fact]
    public void GetToolsForConfig_DefaultConfig_ReturnsTheEntireCatalog()
    {
        var tools = new BaseToolRegistry().GetToolsForConfig(ToolsConfig.Default, Context());

        tools.Select(t => t.Name).Should().Equal(AllToolNames);
        tools.Should().HaveCount(30);
    }

    [Fact]
    public void GetToolsForConfig_BuiltinDisabled_ReturnsNothing()
    {
        new BaseToolRegistry().GetToolsForConfig(new ToolsConfig(BuiltinEnabled: false), Context())
            .Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ToolGroups))]
    public void GetToolsForConfig_DisabledSlug_SkipsOnlyThatGroupsTools(string slug, string[] disabledToolNames)
    {
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            [slug] = new(Enabled: false),
        });

        var names = new BaseToolRegistry().GetToolsForConfig(config, Context()).Select(t => t.Name).ToList();

        names.Should().NotContain(disabledToolNames);
        names.Should().BeEquivalentTo(AllToolNames.Except(disabledToolNames));
        names.Should().HaveCount(30 - disabledToolNames.Length);
    }

    [Theory]
    [MemberData(nameof(ToolGroups))]
    public void GetToolsForConfig_PerToolOverride_DisablesOnlyTheSpecifiedTool(string slug, string[] groupToolNames)
    {
        var disabledTool = groupToolNames[0];
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            [slug] = new(Tools: new Dictionary<string, bool> { [disabledTool] = false }),
        });

        var names = new BaseToolRegistry().GetToolsForConfig(config, Context()).Select(t => t.Name).ToList();

        names.Should().NotContain(disabledTool);
        names.Should().BeEquivalentTo(AllToolNames.Except([disabledTool]));
        names.Should().HaveCount(29);
    }

    [Fact]
    public void GetToolsForConfig_UnlistedSlugsAndTools_DefaultToEnabled()
    {
        // Preserves the legacy "all tools" behaviour for partially-configured workspaces.
        var config = new ToolsConfig(Builtin: new Dictionary<string, SlugToolsConfig>
        {
            ["calculator"] = new(),
        });

        new BaseToolRegistry().GetToolsForConfig(config, Context()).Select(t => t.Name)
            .Should().Equal(AllToolNames);
    }

    [Fact]
    public void Register_ReplacingAnExistingSlug_KeepsItsPosition()
    {
        var registry = new BaseToolRegistry();

        registry.Register(new StubToolModule("time", "get_stub"));

        registry.AllSlugs().Should().Equal(ExpectedSlugs);
        registry.GetTools(["time"], Context()).Select(t => t.Name).Should().Equal(["get_stub"]);
    }

    [Fact]
    public void Register_NewSlug_IsAppendedAndResolvable()
    {
        var registry = new BaseToolRegistry();

        registry.Register(new StubToolModule("custom", "get_custom"));

        registry.AllSlugs().Should().Equal(ExpectedSlugs.Append("custom"));
        registry.GetToolsForConfig(ToolsConfig.Default, Context())
            .Select(t => t.Name).Should().Contain("get_custom");
    }

    [Fact]
    public void Catalog_ContainsEveryGroupAndFunctionInTheCrossPlatformCatalog()
    {
        var catalog = new BaseToolRegistry().Catalog();

        catalog.Keys.Should().Equal(ExpectedSlugs);
        catalog.Should().HaveCount(ExpectedTools.Count);
        foreach (var (slug, toolNames) in ExpectedTools)
        {
            catalog[slug].Select(entry => entry.Name).Should().Equal(toolNames);
            catalog[slug].Should().OnlyContain(entry => !string.IsNullOrWhiteSpace(entry.Doc));
        }

        catalog["calculator"].Single(e => e.Name == "calculate").Operation.Should().Be(OperationKind.Query);
        catalog["weather"].Should().OnlyContain(entry => entry.Operation == OperationKind.Query);
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
    [InlineData("""{"builtin": {"time": {"enabled": false}}}""", 26)]
    [InlineData("""{"builtin": {"time": {"tools": {"get_current_date": false}}}}""", 29)]
    [InlineData("""{}""", 30)]
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

    public static IEnumerable<object[]> ToolGroups() =>
        ExpectedTools.Select(group => new object[] { group.Key, group.Value });

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
