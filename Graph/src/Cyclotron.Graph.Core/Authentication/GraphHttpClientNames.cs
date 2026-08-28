namespace Cyclotron.Graph.Core.Authentication;

/// <summary>
/// Names of the <c>IHttpClientFactory</c>-managed named clients Cyclotron.Graph.Core registers.
/// A consumer may reference these constants to further configure either named client (for
/// example, attaching additional message handlers) without depending on a magic string.
/// </summary>
public static class GraphHttpClientNames
{
    /// <summary>
    /// The named client used to acquire access tokens from the Microsoft Entra login endpoint.
    /// Deliberately a plain named client, not a typed client: <see cref="ClientSecretTokenProvider"/>
    /// is a singleton, and a typed client (<c>AddHttpClient&lt;T&gt;</c>) would pin that singleton
    /// to a single <see cref="System.Net.Http.HttpMessageHandler"/> forever, defeating
    /// <c>IHttpClientFactory</c>'s handler rotation.
    /// </summary>
    public const string Token = "cyclotron-graph-token";

    /// <summary>The named client used to call the Microsoft Graph API itself.</summary>
    public const string Api = "cyclotron-graph-api";
}
