using Azure.Core;
using Azure.Identity;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Authentication;

/// <summary>
/// Acquires and caches Microsoft Graph access tokens via <see cref="DefaultAzureCredential"/>
/// (managed identity, <c>az login</c>, Visual Studio credentials, etc.).
/// </summary>
/// <remarks>
/// MUST be registered as a <b>singleton</b>, for the same reason as <see cref="ClientSecretTokenProvider"/>:
/// the token cache must survive <c>IHttpClientFactory</c>'s handler rotation, so it cannot be a
/// captive dependency of the transient <see cref="GraphAuthHandler"/>.
/// </remarks>
internal sealed class DefaultAzureCredentialTokenProvider : IGraphTokenProvider
{
    private readonly GraphAuthOptions _options;
    private readonly DefaultAzureCredential _credential = new();
    private readonly ILogger<DefaultAzureCredentialTokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AccessToken? _accessToken;
    private DateTimeOffset _expiry = DateTimeOffset.MinValue;

    public DefaultAzureCredentialTokenProvider(
        IOptions<GraphCoreOptions> options,
        ILogger<DefaultAzureCredentialTokenProvider> logger)
    {
        _options = options.Value.Auth;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _expiry)
            return _accessToken.Value.Token;

        await _lock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _expiry)
                return _accessToken.Value.Token;

            _logger.LogDebug("Acquiring Graph API access token via DefaultAzureCredential");

            var context = new TokenRequestContext([_options.Scope]);
            var token = await _credential.GetTokenAsync(context, ct);

            _accessToken = token;
            _expiry = token.ExpiresOn.AddSeconds(-_options.TokenExpiryBufferSeconds);

            _logger.LogDebug("Graph API token acquired, valid until {Expiry:u}", _expiry);
            return token.Token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Graph API access token via DefaultAzureCredential");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
}
