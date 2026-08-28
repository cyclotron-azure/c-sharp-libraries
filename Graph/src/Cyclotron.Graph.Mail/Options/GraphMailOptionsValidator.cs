using Microsoft.Extensions.Options;

namespace Cyclotron.Graph.Mail.Options;

/// <summary>
/// Validates a bound <see cref="GraphMailOptions"/> instance, returning every failing rule in one
/// consolidated result rather than stopping at the first failure — so a misconfigured consumer
/// sees the full list of problems on their first startup failure instead of fixing them one at a
/// time. Validates only mail-owned fields — the shared Graph plumbing package owns and validates
/// its own authentication, subscription, and endpoint options separately, and this validator has
/// no knowledge of them.
/// </summary>
internal sealed class GraphMailOptionsValidator : IValidateOptions<GraphMailOptions>
{
    public ValidateOptionsResult Validate(string? name, GraphMailOptions options)
    {
        var failures = new List<string>();

        ValidateMessage(options.Message, failures);
        ValidateDelta(options.Delta, failures);

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateMessage(GraphMessageOptions message, List<string> failures)
    {
        // Unreachable by design, and retained deliberately. The PostConfigure<GraphMailOptions>
        // registered in AddCyclotronGraphMailCore replaces an empty SelectFields with
        // GraphMessageOptions.DefaultSelectFields before any IValidateOptions runs, so this rule
        // cannot fire while that fallback exists — it documents the invariant the fallback
        // upholds, and would fire again if the fallback were ever removed. Deleting it as dead
        // code silently re-arms the configuration-binding regression task 09a fixed.
        if (message.SelectFields.Count == 0)
        {
            failures.Add("GraphMailOptions.Message.SelectFields must contain at least one field.");
        }

        // Both MAPI tags are only meaningful when ExpandMapiHeaders is true — when it is false, the
        // client never sends the $expand clause that reads them, so requiring them would break a
        // consumer who has deliberately disabled the expansion.
        if (message.ExpandMapiHeaders)
        {
            if (string.IsNullOrWhiteSpace(message.MapiInReplyToTag))
            {
                failures.Add("GraphMailOptions.Message.MapiInReplyToTag must be configured when Message.ExpandMapiHeaders is true.");
            }

            if (string.IsNullOrWhiteSpace(message.MapiReferencesTag))
            {
                failures.Add("GraphMailOptions.Message.MapiReferencesTag must be configured when Message.ExpandMapiHeaders is true.");
            }
        }
    }

    private static void ValidateDelta(GraphDeltaOptions delta, List<string> failures)
    {
        if (string.IsNullOrWhiteSpace(delta.FolderScope))
        {
            failures.Add("GraphMailOptions.Delta.FolderScope must be configured.");
        }

        // null means "reuse Message.SelectFields" (see GraphMailClient.GetMessagesDeltaAsync's
        // `delta.SelectFields ?? message.SelectFields`) and is a valid, intentional configuration.
        // A non-null but EMPTY list is different: it is not a fallback signal, it is an explicit
        // "$select with no fields", which Graph would reject. Only the empty-but-non-null case is
        // a failure.
        if (delta.SelectFields is { Count: 0 })
        {
            failures.Add("GraphMailOptions.Delta.SelectFields, when specified, must contain at least one field.");
        }
    }
}
