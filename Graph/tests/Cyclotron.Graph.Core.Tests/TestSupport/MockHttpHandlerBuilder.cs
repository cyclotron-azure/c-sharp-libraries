using System.Net;
using Moq;
using Moq.Protected;

namespace Cyclotron.Graph.Core.Tests.TestSupport;

/// <summary>
/// A Moq-based builder around <c>Mock&lt;HttpMessageHandler&gt;</c> that records every request's
/// method, URI, and body, and returns caller-scripted responses in sequence. This wraps the mock's
/// protected <c>SendAsync</c> setup rather than subclassing <see cref="HttpMessageHandler"/>.
/// <para>
/// Recording happens inside the single setup's <c>Returns</c> delegate rather than through
/// <c>SetupSequence</c>, because <c>SetupSequence</c> does not support <c>Callback</c> — so a
/// sequenced scenario could not otherwise capture the requests it was asked to assert on.
/// </para>
/// <para>
/// Shared by every test class in this project that drives HTTP. It lives here rather than as a
/// <c>file</c>-scoped type beside its callers because a file-local type cannot appear in the
/// signature of a member of a non-file-local class (CS9051), which is exactly what the
/// <c>CreateClient</c>/<c>CreateProvider</c> helpers need to return.
/// </para>
/// </summary>
internal sealed class MockHttpHandlerBuilder
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _scripted = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _fallback;

    /// <summary>The underlying mock, for protected-member verification.</summary>
    public Mock<HttpMessageHandler> Handler { get; } = new();

    /// <summary>Every request this handler has seen, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>The most recently recorded request. Throws if nothing has been recorded.</summary>
    public RecordedRequest LastRequest => Requests[^1];

    public MockHttpHandlerBuilder()
    {
        Handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                // HttpClient disposes request content once the send completes, so the body has to
                // be read here rather than lazily at assertion time.
                var body = request.Content is null
                    ? null
                    : await request.Content.ReadAsStringAsync(ct);

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
                    $"MockHttpHandlerBuilder received an unscripted request: {request.Method} {request.RequestUri}");
            });
    }

    /// <summary>Creates an <see cref="HttpClient"/> whose handler is this builder's mock.</summary>
    public HttpClient CreateHttpClient(string baseAddress) =>
        new(Handler.Object) { BaseAddress = new Uri(baseAddress) };

    /// <summary>Scripts the next response in sequence as JSON with the given status.</summary>
    public MockHttpHandlerBuilder RespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _scripted.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        return this;
    }

    /// <summary>Scripts the next response in sequence as a bare status code with an empty body.</summary>
    public MockHttpHandlerBuilder RespondWithStatus(HttpStatusCode status)
    {
        _scripted.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(string.Empty)
        });
        return this;
    }

    /// <summary>
    /// Sets the response used once the scripted sequence is exhausted. Without one, an unscripted
    /// request fails the test loudly rather than silently returning a default response.
    /// </summary>
    public MockHttpHandlerBuilder AlwaysRespondWithJson(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        _fallback = _ => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        return this;
    }

    /// <summary>Verifies the mock's protected SendAsync method was invoked exactly <paramref name="count"/> times.</summary>
    public void VerifyRequestCount(int count) =>
        Handler.Protected().Verify(
            "SendAsync", Times.Exactly(count), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
}

/// <summary>One request the mock handler observed.</summary>
internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Body,
    string? Authorization)
{
    /// <summary>The request URI as an unescaped string, for readable substring assertions.</summary>
    public string UriString => Uri.ToString();
}
