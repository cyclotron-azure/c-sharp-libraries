namespace Cyclotron.Graph.Core.Abstractions;

/// <summary>
/// Acquires and caches an access token for calling the Microsoft Graph API.
/// </summary>
/// <remarks>
/// Implementations MUST be registered as a <b>singleton</b>. The token cache needs to survive
/// <c>IHttpClientFactory</c>'s handler rotation (roughly every two minutes) — a transient auth
/// handler is created on each rotation, so the token cache it depends on must outlive that
/// handler's lifetime. Registering the token provider as scoped or transient turns it into a
/// captive dependency of the transient auth handler under <c>IHttpClientFactory</c>, which
/// throws <see cref="ObjectDisposedException"/> once the underlying scope is disposed.
/// </remarks>
public interface IGraphTokenProvider
{
    /// <summary>
    /// Returns a valid access token, acquiring or refreshing it as needed.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<string> GetTokenAsync(CancellationToken ct = default);
}
