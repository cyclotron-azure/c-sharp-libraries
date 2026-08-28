using System.Net;

namespace Cyclotron.Graph.Mail.Tests.TestSupport;

/// <summary>
/// A hand-written <see cref="HttpMessageHandler"/> that records every request's method, URI, and
/// body, and returns caller-scripted responses. Hand-written rather than mocked because the
/// library repo's central package management deliberately declares no mocking library.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _scripted = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    /// <summary>Every request this handler has seen, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>The most recently recorded request. Throws if nothing has been recorded.</summary>
    public RecordedRequest LastRequest => Requests[^1];

    /// <summary>Scripts the next response in sequence as JSON with the given status.</summary>
    public StubHttpMessageHandler RespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _scripted.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        return this;
    }

    /// <summary>Scripts the next response in sequence as a bare status code with an empty body.</summary>
    public StubHttpMessageHandler RespondWithStatus(HttpStatusCode status)
    {
        _scripted.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(string.Empty)
        });
        return this;
    }

    /// <summary>Scripts the next response in sequence with a fully caller-built response.</summary>
    public StubHttpMessageHandler Respond(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _scripted.Enqueue(responder);
        return this;
    }

    /// <summary>
    /// Sets the response used once the scripted sequence is exhausted. Without one, an unscripted
    /// request fails the test loudly rather than silently returning a default response.
    /// </summary>
    public StubHttpMessageHandler AlwaysRespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _fallback = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri!,
            body,
            request.Headers.Authorization?.ToString()));

        if (_scripted.Count > 0)
        {
            return _scripted.Dequeue()(request);
        }

        if (_fallback is not null)
        {
            return _fallback(request);
        }

        throw new InvalidOperationException(
            $"StubHttpMessageHandler received an unscripted request: {request.Method} {request.RequestUri}");
    }
}

/// <summary>One request the stub handler observed.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    string? Authorization)
{
    /// <summary>The request URI as an unescaped string, for readable substring assertions.</summary>
    public string UriString => Uri.ToString();
}
