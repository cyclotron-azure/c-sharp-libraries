using System.Net.Http.Headers;
using Cyclotron.Graph.Core.Abstractions;

namespace Cyclotron.Graph.Core.Authentication;

/// <summary>
/// Stamps outgoing Microsoft Graph requests with a bearer access token.
/// </summary>
/// <remarks>
/// This handler is registered <b>transient</b> and is recreated on each <c>IHttpClientFactory</c>
/// handler-chain rotation (~2 minutes by default). It must therefore only ever capture
/// <b>singleton</b> dependencies — a captured scoped service would be disposed out from under a
/// pooled handler, surfacing as <see cref="ObjectDisposedException"/> on a later request.
/// <paramref name="tokenProvider"/> is safe here because every <see cref="IGraphTokenProvider"/>
/// implementation in this library is itself registered as a singleton.
/// </remarks>
internal sealed class GraphAuthHandler(IGraphTokenProvider tokenProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var token = await tokenProvider.GetTokenAsync(ct);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}
