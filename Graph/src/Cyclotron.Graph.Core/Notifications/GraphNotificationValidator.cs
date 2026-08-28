using System.Security.Cryptography;
using System.Text;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Notifications;

/// <inheritdoc cref="IGraphNotificationValidator" />
internal sealed class GraphNotificationValidator(IOptions<GraphCoreOptions> options) : IGraphNotificationValidator
{
    public bool IsValid(string? clientState)
    {
        if (string.IsNullOrEmpty(clientState))
        {
            return false;
        }

        var expected = options.Value.Subscription.ClientStateSecret;
        if (string.IsNullOrEmpty(expected))
        {
            return false;
        }

        // Constant-time comparison prevents timing-based secret enumeration.
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(clientState);
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}
