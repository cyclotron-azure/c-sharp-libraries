using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Authentication;

/// <summary>
/// Acquires and caches Microsoft Graph access tokens via the OAuth2 client-credentials grant.
/// </summary>
/// <remarks>
/// MUST be registered as a <b>singleton</b>. The token cache held by this instance must survive
/// <c>IHttpClientFactory</c>'s handler rotation (roughly every two minutes) — <see cref="GraphAuthHandler"/>
/// is transient and recreated on each rotation, so the token provider it depends on has to outlive
/// that handler's lifetime. Registering this type as scoped or transient would make it a captive
/// dependency of whatever consumes it, risking <see cref="ObjectDisposedException"/> once the
/// owning scope is disposed.
/// </remarks>
internal sealed class ClientSecretTokenProvider : IGraphTokenProvider
{
    private readonly GraphAuthOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClientSecretTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _expiry = DateTimeOffset.MinValue;

    public ClientSecretTokenProvider(
        IOptions<GraphCoreOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<ClientSecretTokenProvider> logger)
    {
        _options = options.Value.Auth;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiry)
            return _accessToken;

        await _lock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiry)
                return _accessToken;

            _logger.LogDebug("Acquiring Graph API access token for tenant {TenantId}", _options.TenantId);

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"]     = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"]         = _options.Scope,
                ["grant_type"]    = "client_credentials"
            });

            // Obtained from the factory by name, never disposed here: the factory owns the pooled
            // handler and rotates it on its own schedule. Disposing a factory-created client per
            // acquisition (as today's ported code does with `using var client = _http.CreateClient()`)
            // is the wasteful pattern this extraction removes.
            var client = _httpClientFactory.CreateClient(GraphHttpClientNames.Token);
            var response = await client.PostAsync(
                $"{_options.TenantId}/oauth2/v2.0/token", form, ct);
            response.EnsureSuccessStatusCode();

            var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct)
                ?? throw new InvalidOperationException("Empty token response from Azure AD");

            _accessToken = tokenResponse.AccessToken;

            _expiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn - _options.TokenExpiryBufferSeconds);

            _logger.LogDebug("Graph API token acquired, valid until {Expiry:u}", _expiry);
            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Graph API access token");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; init; } = string.Empty;
        [JsonPropertyName("expires_in")]   public int    ExpiresIn   { get; init; }
    }
}
