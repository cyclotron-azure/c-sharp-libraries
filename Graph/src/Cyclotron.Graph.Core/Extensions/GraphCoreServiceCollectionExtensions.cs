using Cyclotron.Graph.Core.Abstractions;
using Cyclotron.Graph.Core.Authentication;
using Cyclotron.Graph.Core.Client;
using Cyclotron.Graph.Core.Notifications;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Extensions;

/// <summary>
/// Registers <c>Cyclotron.Graph.Core</c>'s authentication, HTTP, subscription, and
/// notification-handling services against <see cref="GraphCoreOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lifetime invariant #1 — the token provider MUST be singleton.</b> Its cached access token
/// must survive <c>IHttpClientFactory</c>'s handler-chain rotation (roughly every two minutes).
/// If the token provider were transient or scoped, a fresh instance would be built on every
/// rotation and the token endpoint would be hit every couple of minutes instead of once per
/// actual token expiry.
/// </para>
/// <para>
/// <b>Lifetime invariant #2 — <see cref="GraphAuthHandler"/> MUST be transient, and may only
/// capture singletons.</b> A transient <see cref="System.Net.Http.DelegatingHandler"/> is
/// recreated on every handler-chain rotation. If it captured a scoped service, that service would
/// be disposed out from under a pooled handler once its originating scope ends, surfacing as an
/// <see cref="ObjectDisposedException"/> under load. The handler captures only
/// <see cref="IGraphTokenProvider"/>, which invariant #1 guarantees is a singleton.
/// </para>
/// <para>
/// Do not "simplify" either lifetime — both are load-bearing for correctness, not defaults chosen
/// arbitrarily.
/// </para>
/// </remarks>
public static class GraphCoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>Cyclotron.Graph.Core</c>, binding <see cref="GraphCoreOptions"/> from the
    /// named configuration section.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="configuration">The configuration root to bind <see cref="GraphCoreOptions"/> from.</param>
    /// <param name="sectionName">
    /// The configuration section to bind. A consumer whose settings live under a different key
    /// (for example, an application-specific prefix rather than <see cref="GraphCoreOptions.DefaultSectionName"/>)
    /// supplies that key here.
    /// </param>
    public static IServiceCollection AddCyclotronGraphCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = GraphCoreOptions.DefaultSectionName)
    {
        var section = configuration.GetSection(sectionName);

        services.AddOptions<GraphCoreOptions>()
            .Bind(section)
            .ValidateOnStart();

        // Auth.Mode must be known at REGISTRATION time to pick the right IGraphTokenProvider
        // implementation, but GraphCoreOptions is not bound yet — AddOptions above only wires up
        // binding to run when IOptions<GraphCoreOptions> is first resolved. Resolving
        // IOptions<GraphCoreOptions> here to read Auth.Mode would force the options pipeline to
        // run early, out of order with any .Configure/.PostConfigure a consumer registers after
        // this call, and can deadlock in host-startup scenarios that resolve options before the
        // container is fully built. Reading the raw configuration section directly sidesteps all
        // of that — it is available immediately and reflects exactly what will be bound later.
        var authModeSection = section.GetSection($"{nameof(GraphCoreOptions.Auth)}:{nameof(GraphAuthOptions.Mode)}");
        var authMode = authModeSection.Value is { Length: > 0 } rawMode
            ? Enum.Parse<GraphAuthMode>(rawMode, ignoreCase: true)
            : GraphAuthMode.ClientSecret;

        return services.AddCyclotronGraphCoreCore(authMode);
    }

    /// <summary>
    /// Registers <c>Cyclotron.Graph.Core</c>, configuring <see cref="GraphCoreOptions"/> in code.
    /// Intended for tests and consumers without an <see cref="IConfiguration"/>.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="configure">A delegate that configures <see cref="GraphCoreOptions"/>.</param>
    public static IServiceCollection AddCyclotronGraphCore(
        this IServiceCollection services,
        Action<GraphCoreOptions> configure)
    {
        services.AddOptions<GraphCoreOptions>()
            .Configure(configure)
            .ValidateOnStart();

        // Same registration-time constraint as the IConfiguration overload above: Auth.Mode must
        // be known before options are bound, so we invoke the supplied delegate against a
        // throwaway instance purely to read Auth.Mode. This throwaway instance is never used for
        // anything else — the real, DI-managed GraphCoreOptions instance is built by the options
        // pipeline from the same delegate when IOptions<GraphCoreOptions> is first resolved.
        var probe = new GraphCoreOptions();
        configure(probe);
        var authMode = probe.Auth.Mode;

        return services.AddCyclotronGraphCoreCore(authMode);
    }

    /// <summary>
    /// The shared core both public overloads route through, so the two registration paths cannot
    /// drift from each other.
    /// </summary>
    private static IServiceCollection AddCyclotronGraphCoreCore(this IServiceCollection services, GraphAuthMode authMode)
    {
        services.TryAddSingleton<IValidateOptions<GraphCoreOptions>, GraphCoreOptionsValidator>();

        services.AddHttpClient(GraphHttpClientNames.Token, (sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<GraphCoreOptions>>().Value;
            client.BaseAddress = new Uri(options.LoginBaseAddress);
        });

        // GraphAuthHandler is transient (see the class-level remarks) and must never capture a
        // scoped service — it currently captures only IGraphTokenProvider, a singleton.
        services.AddTransient<GraphAuthHandler>();

        services.AddCyclotronGraphApiHttpClient<IGraphSubscriptionClient, GraphSubscriptionClient>();

        // The token provider MUST be singleton (see the class-level remarks). TryAddSingleton lets
        // a consumer's own IGraphTokenProvider registration — made before or after this call —
        // win over the built-in providers below.
        switch (authMode)
        {
            case GraphAuthMode.ClientSecret:
                services.TryAddSingleton<IGraphTokenProvider, ClientSecretTokenProvider>();
                break;

            case GraphAuthMode.DefaultAzureCredential:
                services.TryAddSingleton<IGraphTokenProvider, DefaultAzureCredentialTokenProvider>();
                break;

            case GraphAuthMode.Custom:
                // Intentionally registers nothing — the consumer supplies their own
                // IGraphTokenProvider implementation, before or after this call.
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(authMode), authMode, "Unrecognized GraphAuthMode.");
        }

        services.TryAddSingleton<IGraphNotificationValidator, GraphNotificationValidator>();

        // Transient: GraphNotificationParser has no scoped dependencies, matching the lifetime of
        // today's notification handlers.
        services.TryAddTransient<IGraphNotificationParser, GraphNotificationParser>();

        // No sink implementation is registered here. The consuming application implements and
        // registers IGraphMessageNotificationSink / IGraphLifecycleNotificationSink itself, at
        // whatever lifetime its own dependencies require (a sink backed by repositories, for
        // example, will typically be scoped).

        return services;
    }

    /// <summary>
    /// Configures the Graph API named/typed <see cref="HttpClient"/> — base address from
    /// <see cref="GraphCoreOptions.BaseAddress"/>, the <c>Prefer</c> header from
    /// <see cref="GraphCoreOptions.PreferHeader"/>, and <see cref="GraphAuthHandler"/> on the
    /// handler chain — and adds it as a typed client for <typeparamref name="TService"/> /
    /// <typeparamref name="TImplementation"/>.
    /// </summary>
    /// <remarks>
    /// Internal so that <c>Cyclotron.Graph.Mail</c>'s <c>AddCyclotronGraphMail</c> can register
    /// <c>IGraphMailClient</c> against the identical base address, header, and auth-handler chain
    /// without duplicating this configuration lambda — see the <c>InternalsVisibleTo</c> attribute
    /// in <c>Properties/AssemblyInfo.cs</c>. This is Core's chosen HTTP-configuration sharing
    /// mechanism for task 09's reuse.
    /// </remarks>
    internal static IHttpClientBuilder AddCyclotronGraphApiHttpClient<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        return services.AddHttpClient<TService, TImplementation>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<GraphCoreOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseAddress);
                client.DefaultRequestHeaders.Add("Prefer", options.PreferHeader);
            })
            .AddHttpMessageHandler<GraphAuthHandler>();
    }
}
