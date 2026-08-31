using System.Net;
using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Extensions;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Mail.Abstractions;
using Cyclotron.Graph.Mail.Extensions;
using Cyclotron.Graph.Mail.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;

namespace Cyclotron.Graph.Mail.Tests;

public class GraphMailServiceCollectionExtensionsTests
{
    /// <summary>
    /// A Moq-backed <see cref="HttpMessageHandler"/> builder that records every request's method,
    /// URI, and body, and returns a caller-scripted fallback response for every request.
    /// <see cref="HttpMessageHandler.SendAsync"/> is protected, so the single underlying
    /// <see cref="Mock{HttpMessageHandler}"/> setup both records the request and returns the
    /// scripted response.
    /// </summary>
    private sealed class ScriptedHandler
    {
        private readonly Mock<HttpMessageHandler> _mock = new();
        private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

        public ScriptedHandler()
        {
            _mock.Protected()
                .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
                .Returns(async (HttpRequestMessage request, CancellationToken cancellationToken) =>
                {
                    var body = request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(cancellationToken);

                    Requests.Add(new RecordedRequest(
                        request.Method,
                        request.RequestUri!,
                        body,
                        request.Headers.Authorization?.ToString()));

                    if (_fallback is not null)
                    {
                        return _fallback(request);
                    }

                    throw new InvalidOperationException(
                        $"ScriptedHandler received an unscripted request: {request.Method} {request.RequestUri}");
                });
        }

        /// <summary>The mocked handler to hand to an <see cref="HttpClient"/> factory.</summary>
        public HttpMessageHandler Handler => _mock.Object;

        /// <summary>Every request this handler has seen, in order.</summary>
        public List<RecordedRequest> Requests { get; } = [];

        /// <summary>
        /// Sets the response used for every request. Without one, an unscripted request fails the
        /// test loudly rather than silently returning a default response.
        /// </summary>
        public ScriptedHandler AlwaysRespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
        {
            _fallback = _ => new HttpResponseMessage(status)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return this;
        }
    }

    /// <summary>One request the scripted handler observed.</summary>
    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        string? Authorization)
    {
        /// <summary>The request URI as an unescaped string, for readable substring assertions.</summary>
        public string UriString => Uri.ToString();
    }

    /// <summary>The nine documented defaults, in their documented order, hard-coded deliberately.</summary>
    private static readonly string[] ExpectedDefaultSelectFields =
    [
        "internetMessageId",
        "subject",
        "receivedDateTime",
        "body",
        "hasAttachments",
        "from",
        "toRecipients",
        "uniqueBody",
        "categories",
    ];

    private static void ConfigureValidCore(GraphCoreOptions options)
    {
        options.Auth.TenantId = "tenant-id";
        options.Auth.ClientId = "client-id";
        options.Auth.ClientSecret = "client-secret";
        options.Subscription.ClientStateSecret = "client-state-secret";
        options.Subscription.NotificationBaseUrl = "https://example.test";
        options.Subscription.NotificationPath = "/notifications";
        options.Subscription.LifecycleNotificationPath = "/lifecycle";
    }

    private static Dictionary<string, string?> ValidCoreConfiguration() => new()
    {
        ["Graph:Auth:TenantId"] = "tenant-id",
        ["Graph:Auth:ClientId"] = "client-id",
        ["Graph:Auth:ClientSecret"] = "client-secret",
        ["Graph:Subscription:ClientStateSecret"] = "client-state-secret",
        ["Graph:Subscription:NotificationBaseUrl"] = "https://example.test",
        ["Graph:Subscription:NotificationPath"] = "/notifications",
        ["Graph:Subscription:LifecycleNotificationPath"] = "/lifecycle",
    };

    [Fact]
    public void AddCyclotronGraphMail_Alone_ResolvesMailClientAndAllCoreServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphMail(ConfigureValidCore, _ => { });

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IGraphMailClient>());
        Assert.NotNull(provider.GetRequiredService<IGraphSubscriptionClient>());
        Assert.NotNull(provider.GetRequiredService<IGraphNotificationValidator>());
        Assert.NotNull(provider.GetRequiredService<IGraphNotificationParser>());
    }

    [Fact]
    public void AddCyclotronGraphCoreThenMail_BothCalled_RegistersTokenProviderExactlyOnce()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphCore(ConfigureValidCore);
        services.AddCyclotronGraphMail(ConfigureValidCore, _ => { });

        Assert.Equal(1, services.Count(d => d.ServiceType == typeof(IGraphTokenProvider)));

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IGraphMailClient>());
        Assert.NotNull(provider.GetRequiredService<IGraphSubscriptionClient>());
    }

    /// <summary>
    /// Issues a real request through each client with the primary handler stubbed, so this asserts
    /// the shared (fixed, commercial-cloud) base address and the auth handler's presence
    /// behaviorally rather than by reading registration descriptors.
    /// </summary>
    [Fact]
    public async Task AddCyclotronGraphMail_BothClients_ShareBaseAddressAndCarryAuthHandler()
    {
        var handler = new ScriptedHandler().AlwaysRespondWithJson("""{ "value": [] }""");
        var services = new ServiceCollection();
        services.AddLogging();
        var tokenProvider = new Mock<IGraphTokenProvider>();
        tokenProvider.Setup(p => p.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("stub-token");
        services.AddSingleton(tokenProvider.Object);
        services.AddCyclotronGraphMail(
            options =>
            {
                ConfigureValidCore(options);
                options.Auth.Mode = GraphAuthMode.Custom;
            },
            mail => mail.Message.SelectFields = ["subject"]);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler.Handler));

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IGraphSubscriptionClient>()
            .GetActiveSubscribedResourceIdsAsync(ct: TestContext.Current.CancellationToken);
        await provider.GetRequiredService<IGraphMailClient>()
            .ResolveMailboxIdAsync("user@contoso.com", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.StartsWith("https://graph.microsoft.com/", r.UriString, StringComparison.Ordinal));
        Assert.All(handler.Requests, r => Assert.Equal("Bearer stub-token", r.Authorization));
    }

    [Fact]
    public async Task AddCyclotronGraphMail_InvalidMailConfiguration_ThrowsOptionsValidationExceptionAtStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddCyclotronGraphMail(
            ConfigureValidCore,
            mail => mail.Delta.FolderScope = string.Empty);
        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains(exception.Failures, f => f.Contains("GraphMailOptions.Delta.FolderScope", StringComparison.Ordinal));
    }

    [Fact]
    public void AddCyclotronGraphMail_SelectFieldsUnconfigured_ResolvesToNineDefaultsInOrder()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidCoreConfiguration())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphMail(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphMailOptions>>().Value;

        Assert.Equal(ExpectedDefaultSelectFields, options.Message.SelectFields);
    }

    /// <summary>
    /// The case task 09a fixed: before that change the binder appended to the pre-populated
    /// initializer, so a consumer configuring one field received ten.
    /// </summary>
    [Fact]
    public void AddCyclotronGraphMail_SelectFieldsFromConfiguration_ResolvesToExactlyThoseFields()
    {
        var settings = ValidCoreConfiguration();
        settings["Graph:Mail:Message:SelectFields:0"] = "subject";
        settings["Graph:Mail:Message:SelectFields:1"] = "body";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphMail(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphMailOptions>>().Value;

        Assert.Equal(["subject", "body"], options.Message.SelectFields);
        Assert.Equal(2, options.Message.SelectFields.Count);
        Assert.DoesNotContain("internetMessageId", options.Message.SelectFields);
    }

    /// <summary>
    /// This path was already correct before 09a; the test guards against the new PostConfigure
    /// fallback regressing it by injecting defaults over a consumer's own list.
    /// </summary>
    [Fact]
    public void AddCyclotronGraphMail_SelectFieldsFromActionOverload_ResolvesToExactlyThoseFields()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphMail(
            ConfigureValidCore,
            mail => mail.Message.SelectFields = ["subject", "body"]);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphMailOptions>>().Value;

        Assert.Equal(["subject", "body"], options.Message.SelectFields);
        Assert.Equal(2, options.Message.SelectFields.Count);
    }

    /// <summary>
    /// The accepted, documented consequence of 09a: an emptiness guard cannot distinguish
    /// "unconfigured" from "deliberately emptied", so both resolve to the nine defaults and the
    /// host starts. The validator's Message.SelectFields non-empty rule is consequently
    /// unreachable through the DI path by design — criterion 48 exercises it directly instead.
    /// </summary>
    [Fact]
    public async Task AddCyclotronGraphMail_SelectFieldsExplicitlyEmptied_ResolvesToNineDefaultsAndStarts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddCyclotronGraphMail(
            ConfigureValidCore,
            mail => mail.Message.SelectFields = []);
        using var host = builder.Build();

        await host.StartAsync(TestContext.Current.CancellationToken);
        var options = host.Services.GetRequiredService<IOptions<GraphMailOptions>>().Value;

        Assert.Equal(ExpectedDefaultSelectFields, options.Message.SelectFields);
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Documents the binder's actual behavior: Microsoft.Extensions.Configuration materializes no
    /// keys for an empty JSON array, so both an absent key and an explicitly empty array leave the
    /// property untouched and it binds to null — meaning "reuse Message.SelectFields". The
    /// empty-but-non-null state is reachable only in code, where the validator rejects it.
    /// </summary>
    [Fact]
    public void AddCyclotronGraphMail_DeltaSelectFieldsAbsentFromConfiguration_BindsToNull()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(ValidCoreConfiguration())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCyclotronGraphMail(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<GraphMailOptions>>().Value;

        Assert.Null(options.Delta.SelectFields);
    }
}
