using Cyclotron.Graph.Core.Notifications;

namespace Cyclotron.Graph.Core.Tests;

public class GraphSubscriptionHandshakeTests
{
    [Fact]
    public void TryGetValidationToken_GraphShapedToken_DecodesPercentEncoding()
    {
        // Real Graph tokens look like "Validation: Testing client application reachability for
        // subscription Request-Id: <guid>" — spaces and colons arrive percent-encoded.
        var query = "?validationToken=Validation%3A%20Testing%20client%20application%20reachability";

        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(query, out var token);

        Assert.True(isHandshake);
        Assert.Equal("Validation: Testing client application reachability", token);
    }

    [Fact]
    public void TryGetValidationToken_PlusEncodedSpaces_Decodes()
    {
        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(
            "validationToken=Validation%3a+Testing", out var token);

        Assert.True(isHandshake);
        Assert.Equal("Validation: Testing", token);
    }

    [Fact]
    public void TryGetValidationToken_LeadingQuestionMarkOptional()
    {
        Assert.True(GraphSubscriptionHandshake.TryGetValidationToken("?validationToken=abc", out var withMark));
        Assert.True(GraphSubscriptionHandshake.TryGetValidationToken("validationToken=abc", out var withoutMark));

        Assert.Equal("abc", withMark);
        Assert.Equal("abc", withoutMark);
    }

    [Fact]
    public void TryGetValidationToken_ParameterNameCaseInsensitive()
    {
        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(
            "?ValidationToken=abc", out var token);

        Assert.True(isHandshake);
        Assert.Equal("abc", token);
    }

    [Fact]
    public void TryGetValidationToken_TokenFoundAmongOtherParameters()
    {
        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(
            "?foo=1&validationToken=abc&bar=2", out var token);

        Assert.True(isHandshake);
        Assert.Equal("abc", token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?foo=1&bar=2")]
    [InlineData("?validationtokenish=abc")]
    public void TryGetValidationToken_NoHandshakeParameter_ReturnsFalse(string? query)
    {
        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(query, out var token);

        Assert.False(isHandshake);
        Assert.Null(token);
    }

    [Theory]
    [InlineData("?validationToken=")]
    [InlineData("?validationToken")]
    public void TryGetValidationToken_EmptyToken_ReturnsFalse(string query)
    {
        // An empty echo cannot validate anything; treat it as an ordinary delivery.
        var isHandshake = GraphSubscriptionHandshake.TryGetValidationToken(query, out var token);

        Assert.False(isHandshake);
        Assert.Null(token);
    }
}
