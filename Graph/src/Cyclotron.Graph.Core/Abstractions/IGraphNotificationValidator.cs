namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Validates the <c>clientState</c> value carried on an incoming Graph notification against the
/// value configured for the subscription.
/// </summary>
public interface IGraphNotificationValidator
{
    /// <summary>
    /// Returns whether <paramref name="clientState"/> matches the configured secret. Implementations
    /// should compare using a constant-time comparison so response timing cannot be used to
    /// infer the secret's value byte by byte.
    /// </summary>
    /// <param name="clientState">The <c>clientState</c> value from the incoming notification, or null if absent.</param>
    bool IsValid(string? clientState);
}
