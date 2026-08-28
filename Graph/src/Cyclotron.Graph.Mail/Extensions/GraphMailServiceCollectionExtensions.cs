using Cyclotron.Graph.Core.Extensions;
using Cyclotron.Graph.Core.Options;
using Cyclotron.Graph.Mail.Abstractions;
using Cyclotron.Graph.Mail.Client;
using Cyclotron.Graph.Mail.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Mail.Extensions;

/// <summary>
/// Registers <c>Cyclotron.Graph.Mail</c>'s <see cref="IGraphMailClient"/> on top of
/// <c>Cyclotron.Graph.Core</c>'s authentication, HTTP, subscription, and notification services.
/// </summary>
public static class GraphMailServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>Cyclotron.Graph.Core</c> and <c>Cyclotron.Graph.Mail</c>, binding both
    /// <see cref="GraphCoreOptions"/> and <see cref="GraphMailOptions"/> from configuration. A mail
    /// consumer makes this single call and cannot forget to also register Core.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="configuration">The configuration root to bind both option types from.</param>
    /// <param name="coreSectionName">The configuration section <see cref="GraphCoreOptions"/> binds from.</param>
    /// <param name="mailSectionName">The configuration section <see cref="GraphMailOptions"/> binds from.</param>
    public static IServiceCollection AddCyclotronGraphMail(
        this IServiceCollection services,
        IConfiguration configuration,
        string coreSectionName = GraphCoreOptions.DefaultSectionName,
        string mailSectionName = GraphMailOptions.DefaultSectionName)
    {
        services.AddCyclotronGraphCore(configuration, coreSectionName);

        var section = configuration.GetSection(mailSectionName);

        services.AddOptions<GraphMailOptions>()
            .Bind(section)
            .ValidateOnStart();

        return services.AddCyclotronGraphMailCore();
    }

    /// <summary>
    /// Registers <c>Cyclotron.Graph.Core</c> and <c>Cyclotron.Graph.Mail</c>, configuring both
    /// option types in code. Intended for tests and consumers without an
    /// <see cref="IConfiguration"/>. A mail consumer makes this single call and cannot forget to
    /// also register Core.
    /// </summary>
    /// <param name="services">The service collection to register against.</param>
    /// <param name="configureCore">A delegate that configures <see cref="GraphCoreOptions"/>.</param>
    /// <param name="configureMail">A delegate that configures <see cref="GraphMailOptions"/>.</param>
    public static IServiceCollection AddCyclotronGraphMail(
        this IServiceCollection services,
        Action<GraphCoreOptions> configureCore,
        Action<GraphMailOptions> configureMail)
    {
        services.AddCyclotronGraphCore(configureCore);

        services.AddOptions<GraphMailOptions>()
            .Configure(configureMail)
            .ValidateOnStart();

        return services.AddCyclotronGraphMailCore();
    }

    /// <summary>
    /// The shared core both public overloads route through, so the two registration paths cannot
    /// drift from each other.
    /// </summary>
    /// <remarks>
    /// <see cref="GraphCoreOptions.Subscription"/>'s <c>ResourceTemplate</c> default
    /// (<c>users/{resourceId}/messages</c>) is already exactly what a mail consumer needs, so no
    /// <c>PostConfigure&lt;GraphCoreOptions&gt;</c> override is applied here — doing so would be
    /// redundant, not corrective. A consumer subscribing to a narrower resource overrides
    /// <c>ResourceTemplate</c> in their own configuration.
    /// </remarks>
    private static IServiceCollection AddCyclotronGraphMailCore(this IServiceCollection services)
    {
        services.TryAddSingleton<IValidateOptions<GraphMailOptions>, GraphMailOptionsValidator>();

        // GraphMessageOptions.SelectFields defaults to empty so the configuration binder, which
        // APPENDS to an already-populated collection rather than replacing it, cannot merge the
        // defaults into a consumer's configured list. The defaults are therefore applied here
        // instead, after every Configure/Bind has run and before ValidateOnStart's validator sees
        // the instance.
        services.PostConfigure<GraphMailOptions>(options =>
        {
            if (options.Message.SelectFields.Count == 0)
            {
                options.Message.SelectFields = GraphMessageOptions.DefaultSelectFields;
            }
        });

        // Reuses Core's internal HTTP-configuration helper so IGraphMailClient shares the exact
        // same base address, Prefer header, and GraphAuthHandler chain as
        // IGraphSubscriptionClient — never hand-copied, so the two clients cannot drift apart.
        services.AddCyclotronGraphApiHttpClient<IGraphMailClient, GraphMailClient>();

        return services;
    }
}
