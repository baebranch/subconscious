using FluentAssertions;
using Subconscious.Engine.Approval;

namespace Subconscious.Engine.Tests.Approval;

public class OperationClassifierTests
{
    [Theory]
    [InlineData("add_todo")]
    [InlineData("run_command")]
    [InlineData("write_clipboard")]
    [InlineData("create_file")]
    public void Classify_ExplicitMutationTools_ReturnsMutation(string toolName)
    {
        OperationClassifier.Classify(toolName).Should().Be(OperationKind.Mutation);
    }

    [Theory]
    [InlineData("get_current_time")]
    [InlineData("list_todos")]
    [InlineData("search_web")]
    [InlineData("read_clipboard")]
    public void Classify_ExplicitQueryTools_ReturnsQuery(string toolName)
    {
        OperationClassifier.Classify(toolName).Should().Be(OperationKind.Query);
    }

    [Theory]
    [InlineData("get_something_new")]
    [InlineData("list_widgets")]
    [InlineData("find_thing")]
    [InlineData("describe_object")]
    public void Classify_UnknownToolWithQueryPrefix_ReturnsQuery(string toolName)
    {
        OperationClassifier.Classify(toolName).Should().Be(OperationKind.Query);
    }

    [Theory]
    [InlineData("do_something_dangerous")]
    [InlineData("unregistered_tool")]
    public void Classify_UnknownToolWithoutQueryPrefix_DefaultsToMutation(string toolName)
    {
        OperationClassifier.Classify(toolName).Should().Be(OperationKind.Mutation);
    }

    [Fact]
    public void Classify_MutationToolWithQueryLikeNameStillMatchesExplicitSet()
    {
        // "recall" is explicitly a query tool even though it doesn't share a query prefix
        // with the heuristic list (it's also listed as a prefix itself).
        OperationClassifier.Classify("recall").Should().Be(OperationKind.Query);
    }
}
