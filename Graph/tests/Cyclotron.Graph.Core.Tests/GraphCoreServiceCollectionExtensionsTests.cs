using System.Net;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Authentication;
using Cyclotron.Graph.Core.Extensions;
using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Core.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Cyclotron.Graph.Core.Tests;

public class GraphCoreServiceCollectionExtensionsTests
{
    private static void ConfigureValid(GraphCoreOptions options)
    {
        options.Auth.TenantId = "tenant-id";
        options.Auth.ClientId = "client-id";
        options.Auth.ClientSecret = "client-secret";
        options.Subscription.ClientStateSecret = "client-state-secret";
        options.Subscription.NotificationBaseUrl = "https://example.test";
        options.Subscription.NotificationPath = "/notifications";
        options.Subscription.LifecycleNotificationPath = "/lifecycle";
    }

    private static ServiceProvider BuildProvider(Action<GraphCoreOptions>? extraConfigure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(options =>
        {
            ConfigureValid(options);
            extraConfigure?.Invoke(options);
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });
    }

    [Fact]
    public void AddCyclotronGraphCore_ValidOptions_ResolvesCoreServicesWithoutMailPackage()
    {
        using var provider = BuildProvider();

        var subscriptionClient = provider.GetRequiredService<IGraphSubscriptionClient>();
        var validator = provider.GetRequiredService<IGraphNotificationValidator>();
        var parser = provider.GetRequiredService<IGraphNotificationParser>();

        Assert.NotNull(subscriptionClient);
        Assert.NotNull(validator);
        Assert.NotNull(parser);
    }

    [Fact]
    public void AddCyclotronGraphCore_TokenProvider_IsRegisteredSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(ConfigureValid);

        var descriptor = services.Single(d => d.ServiceType == typeof(IGraphTokenProvider));

        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<IGraphTokenProvider>(),
            provider.GetRequiredService<IGraphTokenProvider>());
    }

    [Fact]
    public void AddCyclotronGraphCore_GraphAuthHandler_IsRegisteredTransient()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(ConfigureValid);

        var descriptor = services.Single(d => d.ServiceType == typeof(GraphAuthHandler));

        Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
    }

    [Theory]
    [InlineData(GraphAuthMode.ClientSecret, typeof(ClientSecretTokenProvider))]
    [InlineData(GraphAuthMode.DefaultAzureCredential, typeof(DefaultAzureCredentialTokenProvider))]
    public void AddCyclotronGraphCore_AuthMode_RegistersMatchingBuiltInTokenProvider(
        GraphAuthMode mode, Type expectedImplementation)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(options =>
        {
            ConfigureValid(options);
            options.Auth.Mode = mode;
        });

        var descriptor = services.Single(d => d.ServiceType == typeof(IGraphTokenProvider));

        Assert.Equal(expectedImplementation, descriptor.ImplementationType);
    }

    /// <summary>
    /// Adaptation note: the original test registered a hand-written <c>ConsumerTokenProvider</c>
    /// class so the descriptor's <c>ImplementationType</c> was assertable. Registering
    /// <c>tokenProvider.Object</c> instead makes <c>ImplementationType</c> null and populates
    /// <c>ImplementationInstance</c> instead, since Moq mocks are runtime-generated proxy types
    /// rather than the interface type itself. The adapted assertion — that there is exactly one
    /// <see cref="IGraphTokenProvider"/> descriptor, and that the service resolved from the
    /// container is reference-equal to the consumer's own registered instance — is strictly
    /// stronger than the original <c>ImplementationType</c> check: it proves the registration
    /// pipeline left the consumer's own instance in place and resolvable, not merely that a
    /// particular type name survived.
    /// </summary>
    [Fact]
    public void AddCyclotronGraphCore_CustomAuthMode_LeavesConsumerTokenProviderInPlace()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var tokenProvider = new Mock<IGraphTokenProvider>();
        services.AddSingleton(tokenProvider.Object);

        services.AddCyclotronGraphCore(options =>
        {
            ConfigureValid(options);
            options.Auth.Mode = GraphAuthMode.Custom;
        });

        Assert.Single(services, d => d.ServiceType == typeof(IGraphTokenProvider));
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IGraphTokenProvider>();

        Assert.Same(tokenProvider.Object, resolved);
    }

    [Fact]
    public async Task AddCyclotronGraphCore_InvalidConfiguration_ThrowsOptionsValidationExceptionAtStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddCyclotronGraphCore(_ => { });
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Equal(7, exception.Failures.Count());
    }

    [Fact]
    public void AddCyclotronGraphCore_NonDefaultSectionName_BindsEveryNestedValueExactly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MyGraph:Auth:TenantId"] = "t",
                ["MyGraph:Auth:ClientId"] = "c",
                ["MyGraph:Auth:ClientSecret"] = "s",
                ["MyGraph:Subscription:ClientStateSecret"] = "css",
                ["MyGraph:Subscription:NotificationBaseUrl"] = "https://example.test",
                ["MyGraph:Subscription:NotificationPath"] = "/notifications",
                ["MyGraph:Subscription:LifecycleNotificationPath"] = "/lifecycle",
                ["MyGraph:Subscription:ChangeTypes"] = "Created, Deleted",
                ["MyGraph:Subscription:LifespanMinutes"] = "4321",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(configuration, "MyGraph");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphCoreOptions>>().Value;

        Assert.Equal("t", options.Auth.TenantId);
        Assert.Equal("c", options.Auth.ClientId);
        Assert.Equal("s", options.Auth.ClientSecret);
        Assert.Equal("css", options.Subscription.ClientStateSecret);
        Assert.Equal(GraphChangeType.Created | GraphChangeType.Deleted, options.Subscription.ChangeTypes);
        Assert.Equal(4321, options.Subscription.LifespanMinutes);
    }

    /// <summary>
    /// The captive-dependency invariant's <b>preconditions</b>. This proves the token provider is
    /// shared across scope boundaries and that GraphAuthHandler has no container-detectable scoped
    /// capture. It does not simulate IHttpClientFactory handler rotation, does not advance time,
    /// and does not reproduce an ObjectDisposedException — those would require driving the
    /// factory's two-minute rotation, which is out of scope for a unit test.
    /// </summary>
    [Fact]
    public void AddCyclotronGraphCore_TokenProvider_IsSharedAcrossScopesAndHandlerResolvesFromRoot()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(ConfigureValid);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        });

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var fromScopeA = scopeA.ServiceProvider.GetRequiredService<IGraphTokenProvider>();
        var fromScopeB = scopeB.ServiceProvider.GetRequiredService<IGraphTokenProvider>();
        var handlerFromRoot = provider.GetRequiredService<GraphAuthHandler>();

        Assert.Equal(
            ServiceLifetime.Singleton,
            services.Single(d => d.ServiceType == typeof(IGraphTokenProvider)).Lifetime);
        Assert.Same(fromScopeA, fromScopeB);
        Assert.NotNull(handlerFromRoot);
    }
}

/// <summary>
/// Behavioral coverage for ClientSecretTokenProvider — the double-checked token cache, the expiry
/// buffer arithmetic, the posted form shape, and the failure path. Lives in this file (rather than
/// a file of its own) because task 12's write fence enumerates the Core.Tests files by name, and
/// this is the file that already owns the token-provider registration assertions these complete.
/// </summary>
public class ClientSecretTokenProviderBehaviorTests
{
    private const string TenantId = "tenant-id";

    private static (ClientSecretTokenProvider Provider, MockHttpHandlerBuilder Handler) CreateProvider(
        int tokenExpiryBufferSeconds = 90)
    {
        var options = new GraphCoreOptions
        {
            Auth =
            {
                TenantId = TenantId,
                ClientId = "client-id",
                ClientSecret = "client-secret",
                Scope = "https://graph.microsoft.com/.default",
                TokenExpiryBufferSeconds = tokenExpiryBufferSeconds,
            },
        };

        var handler = new MockHttpHandlerBuilder();
        var httpClient = handler.CreateHttpClient("https://login.test/");

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(httpClient);

        var provider = new ClientSecretTokenProvider(
            Microsoft.Extensions.Options.Options.Create(options),
            httpClientFactory.Object,
            NullLogger<ClientSecretTokenProvider>.Instance);

        return (provider, handler);
    }

    private static string TokenJson(string accessToken, int expiresIn) =>
        $$"""{ "access_token": "{{accessToken}}", "expires_in": {{expiresIn}} }""";

    [Fact]
    public async Task GetTokenAsync_CalledTwiceWithUnexpiredToken_IssuesExactlyOneRequest()
    {
        var (provider, handler) = CreateProvider(tokenExpiryBufferSeconds: 90);
        handler.RespondWithJson(TokenJson("token-1", expiresIn: 3600));

        var first = await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        handler.VerifyRequestCount(1);
        Assert.Equal("token-1", first);
        Assert.Equal(first, second);
    }

    /// <summary>
    /// With expires_in smaller than the configured buffer, UtcNow + (expires_in - buffer) lands in
    /// the past, so the cached token is already considered expired and the second call refreshes.
    /// </summary>
    [Fact]
    public async Task GetTokenAsync_ExpiresInSmallerThanBuffer_IssuesSecondRequestOnNextCall()
    {
        var (provider, handler) = CreateProvider(tokenExpiryBufferSeconds: 300);
        handler.RespondWithJson(TokenJson("token-1", expiresIn: 10));
        handler.RespondWithJson(TokenJson("token-2", expiresIn: 10));

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);
        var second = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        handler.VerifyRequestCount(2);
        Assert.Equal("token-2", second);
    }

    [Fact]
    public async Task GetTokenAsync_TokenRequest_PostsClientCredentialsFormToTenantTokenPath()
    {
        var (provider, handler) = CreateProvider();
        handler.RespondWithJson(TokenJson("token-1", expiresIn: 3600));

        await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        var request = handler.LastRequest;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains($"{TenantId}/oauth2/v2.0/token", request.UriString, StringComparison.Ordinal);
        Assert.Contains("grant_type=client_credentials", request.Body!, StringComparison.Ordinal);
        Assert.Contains("client_id=client-id", request.Body!, StringComparison.Ordinal);
        Assert.Contains("scope=https", request.Body!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed acquisition must not cache anything. The provider is a singleton, so a bad token
    /// cached here would persist for the entire process lifetime.
    /// </summary>
    [Fact]
    public async Task GetTokenAsync_FailedAcquisitionThenRetry_DoesNotCacheAndReturnsGoodToken()
    {
        var (provider, handler) = CreateProvider();
        handler.RespondWithStatus(HttpStatusCode.InternalServerError);
        handler.RespondWithJson(TokenJson("good-token", expiresIn: 3600));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.GetTokenAsync(TestContext.Current.CancellationToken));
        var afterRetry = await provider.GetTokenAsync(TestContext.Current.CancellationToken);

        handler.VerifyRequestCount(2);
        Assert.Equal("good-token", afterRetry);
    }

    [Fact]
    public async Task GetTokenAsync_EmptyResponseBody_ThrowsInvalidOperationException()
    {
        var (provider, handler) = CreateProvider();
        handler.RespondWithJson("null");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.GetTokenAsync(TestContext.Current.CancellationToken));
    }
}
