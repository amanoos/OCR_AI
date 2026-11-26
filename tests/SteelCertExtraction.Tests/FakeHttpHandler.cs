using System.Net;
using System.Text;

namespace SteelCertExtraction.Tests;

/// <summary>
/// Captures the outgoing request (including its body, read eagerly because the
/// content is disposed after sending) and returns a canned response.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _responseBody;

    public HttpRequestMessage? Request { get; private set; }
    public string? RequestBody { get; private set; }

    public FakeHttpHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _status = status;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        RequestBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return new HttpResponseMessage(_status)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        };
    }
}
