namespace CoffeeMachine.Client.Tests;

/// <summary>Captures requests and answers them with a caller-supplied responder.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responder;

    public StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) => _responder = responder;

    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return await _responder(request).ConfigureAwait(false);
    }
}
