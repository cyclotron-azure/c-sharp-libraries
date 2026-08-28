using Cyclotron.Graph.Core.Notifications;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Tests;

public class GraphNotificationValidatorTests
{
    [Theory]
    [InlineData("secret", null, false)]
    [InlineData("secret", "", false)]
    [InlineData("secret", "wrong", false)]
    [InlineData("secret", "secret", true)]
    [InlineData("", "", false)]
    [InlineData("secret", "Secret", false)]
    public void IsValid_ConfiguredSecretAndSuppliedClientState_ReturnsExpected(
        string configuredSecret, string? suppliedClientState, bool expected)
    {
        var options = new GraphCoreOptions();
        options.Subscription.ClientStateSecret = configuredSecret;
        var validator = new GraphNotificationValidator(Microsoft.Extensions.Options.Options.Create(options));

        var actual = validator.IsValid(suppliedClientState);

        Assert.Equal(expected, actual);
    }
}
