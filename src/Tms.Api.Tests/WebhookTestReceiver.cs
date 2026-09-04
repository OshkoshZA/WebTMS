using System.Net;

namespace Tms.Api.Tests;

/// <summary>
/// Stands in for a partner's callback endpoint — a real HTTP listener on an ephemeral
/// loopback port, so WebhookDeliveryTests can assert on the actual signed request the
/// platform sent, not a mocked stand-in for it. One instance per test (see the `using`
/// at each call site) rather than shared across the collection, so a test picking
/// RespondWith(500) can't affect another test running the same second.
/// </summary>
public sealed class WebhookTestReceiver : IDisposable
{
    private readonly HttpListener _listener;
    private readonly List<ReceivedRequest> _requests = new();
    private volatile int _responseStatusCode = 200;

    public string Url { get; }
    public IReadOnlyList<ReceivedRequest> Requests { get { lock (_requests) return _requests.ToList(); } }

    public WebhookTestReceiver()
    {
        var port = GetFreeLoopbackPort();
        Url = $"http://127.0.0.1:{port}/hook/";
        _listener = new HttpListener();
        _listener.Prefixes.Add(Url);
        _listener.Start();
        _ = Task.Run(AcceptLoopAsync);
    }

    public void RespondWith(int statusCode) => _responseStatusCode = statusCode;

    private async Task AcceptLoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return; // Stop()/Dispose() was called while GetContextAsync was pending.
            }

            using var reader = new StreamReader(context.Request.InputStream);
            var body = await reader.ReadToEndAsync();
            var signature = context.Request.Headers["X-Tms-Signature"];

            lock (_requests) _requests.Add(new ReceivedRequest(body, signature));

            context.Response.StatusCode = _responseStatusCode;
            context.Response.Close();
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _listener.Stop();
        _listener.Close();
    }

    public sealed record ReceivedRequest(string Body, string? Signature);
}
