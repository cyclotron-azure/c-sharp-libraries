using Cyclotron.Graph.Core.Models;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Options;

/// <summary>
/// Validates a bound <see cref="GraphCoreOptions"/> instance, returning every failing rule in one
/// consolidated result rather than stopping at the first failure — so a misconfigured consumer
/// sees the full list of problems on their first startup failure instead of fixing them one at a
/// time. Validates only Core-owned fields — the mail workload package owns and validates its own
/// resource-specific options separately, and this validator has no knowledge of them.
/// </summary>
internal sealed class GraphCoreOptionsValidator : IValidateOptions<GraphCoreOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphCoreOptions options)
    {
        var failures = new List<string>();

        ValidateAuth(options.Auth, failures);
        ValidateSubscription(options.Subscription, failures);
        ValidateEndpoints(options, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateAuth(GraphAuthOptions auth, List<string> failures)
    {
        // TenantId/ClientId/ClientSecret are only meaningful for the ClientSecret grant — under
        // DefaultAzureCredential or Custom, requiring them would break the managed-identity path.
        if (auth.Mode == GraphAuthMode.ClientSecret)
        {
            if (string.IsNullOrWhiteSpace(auth.TenantId))
            {
                failures.Add("GraphCoreOptions.Auth.TenantId must be configured when Auth.Mode is ClientSecret.");
            }

            if (string.IsNullOrWhiteSpace(auth.ClientId))
            {
                failures.Add("GraphCoreOptions.Auth.ClientId must be configured when Auth.Mode is ClientSecret.");
            }

            if (string.IsNullOrWhiteSpace(auth.ClientSecret))
            {
                failures.Add("GraphCoreOptions.Auth.ClientSecret must be configured when Auth.Mode is ClientSecret.");
            }
        }

        if (string.IsNullOrWhiteSpace(auth.Scope))
        {
            failures.Add("GraphCoreOptions.Auth.Scope must be configured.");
        }

        if (auth.TokenExpiryBufferSeconds < 0)
        {
            failures.Add("GraphCoreOptions.Auth.TokenExpiryBufferSeconds must be greater than or equal to 0.");
        }
    }

    private static void ValidateSubscription(GraphSubscriptionOptions subscription, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(subscription.ClientStateSecret))
        {
            failures.Add("GraphCoreOptions.Subscription.ClientStateSecret must be configured.");
        }

        if (!IsAbsoluteUri(subscription.NotificationUrl))
        {
            failures.Add("GraphCoreOptions.Subscription.NotificationUrl must be an absolute URI.");
        }

        if (!IsAbsoluteUri(subscription.LifecycleNotificationUrl))
        {
            failures.Add("GraphCoreOptions.Subscription.LifecycleNotificationUrl must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(subscription.ResourceTemplate) ||
            !subscription.ResourceTemplate.Contains("{resourceId}", StringComparison.Ordinal))
        {
            failures.Add("GraphCoreOptions.Subscription.ResourceTemplate must be configured and contain the {resourceId} token.");
        }

        if (subscription.ChangeTypes == GraphChangeType.None)
        {
            failures.Add("GraphCoreOptions.Subscription.ChangeTypes must not be None.");
        }

        if (subscription.LifespanMinutes is < 1 or > 10_080)
        {
            failures.Add("GraphCoreOptions.Subscription.LifespanMinutes must be between 1 and 10080.");
        }

        if (subscription.RenewWindowHours is < 1 or > 24)
        {
            failures.Add("GraphCoreOptions.Subscription.RenewWindowHours must be between 1 and 24.");
        }
    }

    private static void ValidateEndpoints(GraphCoreOptions options, List<string> failures)
    {
        if (!IsAbsoluteUri(options.BaseAddress))
        {
            failures.Add("GraphCoreOptions.BaseAddress must be an absolute URI.");
        }

        if (!IsAbsoluteUri(options.LoginBaseAddress))
        {
            failures.Add("GraphCoreOptions.LoginBaseAddress must be an absolute URI.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiVersion))
        {
            failures.Add("GraphCoreOptions.ApiVersion must be configured.");
        }
    }

    private static bool IsAbsoluteUri(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Uri.TryCreate(value, UriKind.Absolute, out _);
}
