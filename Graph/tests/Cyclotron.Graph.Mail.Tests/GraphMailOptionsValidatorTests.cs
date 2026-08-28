using Cyclotron.Graph.Mail.Options;
using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Mail.Tests;

/// <summary>
/// Invokes GraphMailOptionsValidator directly, where every rule is reachable. The
/// Message.SelectFields rule is unreachable through the DI path by design — the PostConfigure
/// fallback supplies the nine defaults before the validator runs — so exercising it requires
/// calling the validator itself.
/// </summary>
public class GraphMailOptionsValidatorTests
{
    private static GraphMailOptions ValidOptions() => new()
    {
        Message = { SelectFields = ["subject", "body"] },
    };

    private static ValidateOptionsResult Validate(GraphMailOptions options) =>
        new GraphMailOptionsValidator().Validate(name: null, options);

    [Fact]
    public void Validate_ValidOptions_Succeeds()
    {
        var options = ValidOptions();

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_EmptyMessageSelectFields_FailsNamingThatOptionPath()
    {
        var options = ValidOptions();
        options.Message.SelectFields = [];

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("GraphMailOptions.Message.SelectFields must contain at least one field.", result.Failures!);
    }

    [Theory]
    [InlineData("MapiInReplyToTag", "GraphMailOptions.Message.MapiInReplyToTag must be configured when Message.ExpandMapiHeaders is true.")]
    [InlineData("MapiReferencesTag", "GraphMailOptions.Message.MapiReferencesTag must be configured when Message.ExpandMapiHeaders is true.")]
    public void Validate_ExpandMapiHeadersWithEmptyTag_FailsNamingThatOptionPath(string tag, string expectedMessage)
    {
        var options = ValidOptions();
        options.Message.ExpandMapiHeaders = true;
        if (tag == "MapiInReplyToTag")
        {
            options.Message.MapiInReplyToTag = string.Empty;
        }
        else
        {
            options.Message.MapiReferencesTag = string.Empty;
        }

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains(expectedMessage, result.Failures!);
    }

    [Fact]
    public void Validate_BothMapiTagsEmptyWithExpansionDisabled_Succeeds()
    {
        var options = ValidOptions();
        options.Message.ExpandMapiHeaders = false;
        options.Message.MapiInReplyToTag = string.Empty;
        options.Message.MapiReferencesTag = string.Empty;

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyDeltaFolderScope_FailsNamingThatOptionPath(string folderScope)
    {
        var options = ValidOptions();
        options.Delta.FolderScope = folderScope;

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("GraphMailOptions.Delta.FolderScope must be configured.", result.Failures!);
    }

    /// <summary>
    /// The null/empty asymmetry is intentional: null means "reuse Message.SelectFields", while a
    /// non-null empty list is an explicit "$select with no fields", which Graph would reject.
    /// </summary>
    [Fact]
    public void Validate_NonNullEmptyDeltaSelectFields_FailsNamingThatOptionPath()
    {
        var options = ValidOptions();
        options.Delta.SelectFields = [];

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.Contains("GraphMailOptions.Delta.SelectFields, when specified, must contain at least one field.", result.Failures!);
    }

    [Fact]
    public void Validate_NullDeltaSelectFields_Succeeds()
    {
        var options = ValidOptions();
        options.Delta.SelectFields = null;

        var result = Validate(options);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_MultipleViolations_ReportsAllOfThem()
    {
        var options = new GraphMailOptions
        {
            Message = { SelectFields = [] },
            Delta = { FolderScope = string.Empty },
        };

        var result = Validate(options);

        Assert.False(result.Succeeded);
        Assert.True(result.Failures!.Count() > 1);
        Assert.Contains("GraphMailOptions.Message.SelectFields must contain at least one field.", result.Failures!);
        Assert.Contains("GraphMailOptions.Delta.FolderScope must be configured.", result.Failures!);
    }
}
