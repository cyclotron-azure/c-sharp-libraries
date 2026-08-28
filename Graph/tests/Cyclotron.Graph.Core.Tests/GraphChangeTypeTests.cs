using Cyclotron.Graph.Core.Models;

namespace Cyclotron.Graph.Core.Tests;

public class GraphChangeTypeTests
{
    [Theory]
    [InlineData("created,updated", GraphChangeType.Created | GraphChangeType.Updated)]
    [InlineData("Created, Updated", GraphChangeType.Created | GraphChangeType.Updated)]
    [InlineData("created", GraphChangeType.Created)]
    [InlineData("created,bogus,deleted", GraphChangeType.Created | GraphChangeType.Deleted)]
    [InlineData(null, GraphChangeType.None)]
    [InlineData("", GraphChangeType.None)]
    [InlineData("   ", GraphChangeType.None)]
    public void Parse_WireFormValue_ReturnsExpectedFlags(string? wireValue, GraphChangeType expected)
    {
        var actual = GraphChangeTypeExtensions.Parse(wireValue);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(GraphChangeType.Created | GraphChangeType.Updated | GraphChangeType.Deleted, "created,updated,deleted")]
    [InlineData(GraphChangeType.Deleted | GraphChangeType.Created, "created,deleted")]
    [InlineData(GraphChangeType.None, "")]
    public void ToGraphString_FlagsValue_RendersLowercaseInDeclarationOrder(GraphChangeType value, string expected)
    {
        var actual = value.ToGraphString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Parse_ThenToGraphString_ReturnsNormalizedWireForm()
    {
        const string input = "Updated, Created";

        var actual = GraphChangeTypeExtensions.Parse(input).ToGraphString();

        Assert.Equal("created,updated", actual);
    }

    [Fact]
    public void FlagOverlap_MaskedAgainstMemberAndNonMember_ReturnsNonZeroThenZero()
    {
        const GraphChangeType covered = GraphChangeType.Created | GraphChangeType.Updated;

        var overlapsCreated = covered & GraphChangeType.Created;
        var overlapsDeleted = covered & GraphChangeType.Deleted;

        Assert.NotEqual(GraphChangeType.None, overlapsCreated);
        Assert.Equal(GraphChangeType.None, overlapsDeleted);
    }
}
