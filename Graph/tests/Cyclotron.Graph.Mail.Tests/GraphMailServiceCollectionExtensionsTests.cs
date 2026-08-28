using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Extensions;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Mail.Abstractions;
using Cyclotron.Graph.Mail.Extensions;
using Cyclotron.Graph.Mail.Options;
using Cyclotron.Graph.Mail.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Mail.Tests;

public class GraphMailServiceCollectionExtensionsTests
{
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
        options.Subscription.NotificationUrl = "https://example.test/notifications";
        options.Subscription.LifecycleNotificationUrl = "https://example.test/lifecycle";
    }

    private static Dictionary<string, string?> ValidCoreConfiguration() => new()
    {
        ["Graph:Auth:TenantId"] = "tenant-id",
        ["Graph:Auth:ClientId"] = "client-id",
        ["Graph:Auth:ClientSecret"] = "client-secret",
        ["Graph:Subscription:ClientStateSecret"] = "client-state-secret",
        ["Graph:Subscription:NotificationUrl"] = "https://example.test/notifications",
        ["Graph:Subscription:LifecycleNotificationUrl"] = "https://example.test/lifecycle",
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
    /// the shared base address and the auth handler's presence behaviorally rather than by reading
    /// registration descriptors.
    /// </summary>
    [Fact]
    public async Task AddCyclotronGraphMail_BothClients_ShareBaseAddressAndCarryAuthHandler()
    {
        var handler = new StubHttpMessageHandler().AlwaysRespondWithJson("""{ "value": [] }""");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IGraphTokenProvider>(new StubTokenProvider());
        services.AddCyclotronGraphMail(
            options =>
            {
                ConfigureValidCore(options);
                options.Auth.Mode = GraphAuthMode.Custom;
                options.BaseAddress = "https://graph.test/";
            },
            mail => mail.Message.SelectFields = ["subject"]);
        services.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IGraphSubscriptionClient>()
            .GetActiveSubscribedResourceIdsAsync(ct: TestContext.Current.CancellationToken);
        await provider.GetRequiredService<IGraphMailClient>()
            .ResolveMailboxIdAsync("user@contoso.com", TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.StartsWith("https://graph.test/", r.UriString, StringComparison.Ordinal));
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

    private sealed class StubTokenProvider : IGraphTokenProvider
    {
        public Task<string> GetTokenAsync(CancellationToken ct = default) => Task.FromResult("stub-token");
    }
}
