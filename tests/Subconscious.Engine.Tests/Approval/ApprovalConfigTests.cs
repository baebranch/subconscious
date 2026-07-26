using FluentAssertions;
using Subconscious.Engine.Approval;

namespace Subconscious.Engine.Tests.Approval;

public class ApprovalConfigTests
{
    [Fact]
    public void Default_RequiresApprovalForBothKinds()
    {
        ApprovalConfig.Default.RequiresApproval(OperationKind.Query).Should().BeTrue();
        ApprovalConfig.Default.RequiresApproval(OperationKind.Mutation).Should().BeTrue();
    }

    [Fact]
    public void RequiresApproval_RespectsPerKindOverrides()
    {
        var config = new ApprovalConfig(RequireApprovalForQueries: false, RequireApprovalForMutations: true);

        config.RequiresApproval(OperationKind.Query).Should().BeFalse();
        config.RequiresApproval(OperationKind.Mutation).Should().BeTrue();
    }
}
