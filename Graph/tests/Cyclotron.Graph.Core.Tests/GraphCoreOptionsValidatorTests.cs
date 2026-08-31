using Cyclotron.Graph.Core.Models;
using Cyclotron.Graph.Core.Options;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Core.Tests;

public class GraphCoreOptionsValidatorTests
{
    private static GraphCoreOptions ValidOptions() => new()
    {
        Auth =
        {
            Mode = GraphAuthMode.ClientSecret,
            TenantId = "tenant-id",
            ClientId = "client-id",
            ClientSecret = "client-secret",
        },
        Subscription =
        {
            ClientStateSecret = "client-state-secret",
            NotificationBaseUrl = "https://example.test",
            NotificationPath = "/notifications",
            LifecycleNotificationPath = "/lifecycle",
        },
    };

    private static ValidateOptionsResult Validate(GraphCoreOptions options) =>
        new GraphCoreOptionsValidator().Validate(name: null, options);

    [Fact]
    public void Validate_FullyPopulatedOptions_Succeeds()
    {
        var options = ValidOptions();

        var result = Validate(options);

        Assert.True(result.Succeeded);
        Assert.True(result.Failures is null || !result.Failures.Any());
    }

    [Theory]
    [InlineData("TenantId", "GraphCoreOptions.Auth.TenantId")]
    [InlineData("ClientId", "GraphCoreOptions.Auth.ClientId")]
    [InlineData("ClientSecret", "GraphCoreOptions.Auth.ClientSecret")]
    public void Validate_MissingClientSecretField_FailsNamingThatOptionPath(string field, string expectedPath)
    {
        var options = ValidOptions();
        switch (field)
        {
            case "TenantId":
                options.Auth.TenantId = string.Empty;
                break;
            case "ClientId":
                options.Auth.ClientId = string.Empty;
                break;
            default:
                options.Auth.ClientSecret = string.Empty;
                break;
        }

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains(expectedPath, StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_EmptyClientStateSecret_FailsNamingThatOptionPath()
    {
        var options = ValidOptions();
        options.Subscription.ClientStateSecret = string.Empty;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.ClientStateSecret", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_ResourceTemplateWithoutToken_FailsNamingThatOptionPath()
    {
        var options = ValidOptions();
        options.Subscription.ResourceTemplate = "users/fixed-id/messages";

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.ResourceTemplate", StringComparison.Ordinal));
    }

    [Theory]
    // A rooted path is what Windows and Linux disagree about: Uri.TryCreate(UriKind.Absolute)
    // rejects it on Windows but accepts it as file:///relative/notifications on Linux. The
    // explicit file:// case pins the same rule without depending on the host platform.
    [InlineData("/relative/notifications")]
    [InlineData("file:///relative/notifications")]
    [InlineData("relative/notifications")]
    public void Validate_NonHttpNotificationBaseUrl_FailsNamingThatOptionPath(string notificationBaseUrl)
    {
        var options = ValidOptions();
        options.Subscription.NotificationBaseUrl = notificationBaseUrl;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.NotificationBaseUrl", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(10_080, true)]
    [InlineData(10_081, false)]
    public void Validate_LifespanMinutesAtBoundary_SucceedsOnlyInsideRange(int lifespanMinutes, bool expectedSucceeded)
    {
        var options = ValidOptions();
        options.Subscription.LifespanMinutes = lifespanMinutes;

        var result = Validate(options);

        Assert.Equal(expectedSucceeded, result.Succeeded);

        if (!expectedSucceeded)
        {
            Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.LifespanMinutes", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(24, true)]
    [InlineData(25, false)]
    public void Validate_RenewWindowHoursAtBoundary_SucceedsOnlyInsideRange(int renewWindowHours, bool expectedSucceeded)
    {
        var options = ValidOptions();
        options.Subscription.RenewWindowHours = renewWindowHours;

        var result = Validate(options);

        Assert.Equal(expectedSucceeded, result.Succeeded);

        if (!expectedSucceeded)
        {
            Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.RenewWindowHours", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Validate_ChangeTypesNone_FailsNamingThatOptionPath()
    {
        var options = ValidOptions();
        options.Subscription.ChangeTypes = GraphChangeType.None;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.ChangeTypes", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(GraphAuthMode.DefaultAzureCredential)]
    [InlineData(GraphAuthMode.Custom)]
    public void Validate_NonClientSecretModeWithNoCredentials_Succeeds(GraphAuthMode mode)
    {
        var options = ValidOptions();
        options.Auth.Mode = mode;
        options.Auth.TenantId = string.Empty;
        options.Auth.ClientId = string.Empty;
        options.Auth.ClientSecret = string.Empty;

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_DefaultConstructedOptions_ReportsAllSevenViolations()
    {
        var options = new GraphCoreOptions();

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Equal(7, result.Failures!.Count());
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Auth.TenantId", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.ClientStateSecret", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.NotificationBaseUrl", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.NotificationPath", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, f => f.Contains("GraphCoreOptions.Subscription.LifecycleNotificationPath", StringComparison.Ordinal));
    }

    /// <summary>
    /// Core must validate purely Core-owned fields. This test compiles and passes only because
    /// GraphCoreOptionsValidator references no mail-owned field — combined with this project's
    /// reference boundary (it references Cyclotron.Graph.Core alone), it is the guard that Core is
    /// independently usable without the mail workload package.
    /// </summary>
    [Fact]
    public void Validate_ValidCoreOptionsWithNoMailPackagePresent_Succeeds()
    {
        var options = ValidOptions();

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }
}
