using System.Net;

namespace Tms.Api.Tests;

/// <summary>
/// Stands in for a partner's callback endpoint — a real HTTP listener on an ephemeral
/// loopback port, so WebhookDeliveryTests can assert on the actual signed request the
/// platform sent, not a mocked stand-in for it. One instance per test (see the `using`
/// at each call site) rather than shared across the collection, so a test picking
/// RespondWith(500) can't affect another test running the same second.
///
/// The dev SQL Server database this suite runs against is shared and never reset
/// (StaffTestFixture), so WebhookSubscriptions rows from long-past runs sit around
/// forever, still Active, still matching any future invoice/credit-note/etc. issued
/// anywhere in the whole suite for the same seeded demo company — and the initial
/// delivery attempt is synchronous and inline regardless (WebhookRetryJob only ever
/// picks up what's already Failed), so a stale subscription gets a real HTTP attempt
/// fired at its old callback URL the moment such an event next fires. Because the OS
/// reissues ephemeral loopback ports, that old URL's port
/// can and does get handed to a brand-new instance of this class. The unique path
/// segment below is what stops that from becoming cross-test contamination: a stale
/// subscription's URL points at some *other* instance's path, so http.sys simply has
/// no listener registered for it and answers 404 without this instance ever seeing the
/// request — instead of the request silently landing in Requests as if it were ours.
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
        var instanceToken = Guid.NewGuid().ToString("N");
        const int maxAttempts = 5;
        Exception? lastError = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var port = GetFreeLoopbackPort();
            var url = $"http://127.0.0.1:{port}/hook/{instanceToken}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            try
            {
                listener.Start();
                Url = url;
                _listener = listener;
                _ = Task.Run(AcceptLoopAsync);
                return;
            }
            catch (HttpListenerException ex)
            {
                // Another process (or another WebhookTestReceiver started concurrently by a
                // test in a different xUnit collection) grabbed this same ephemeral port
                // between GetFreeLoopbackPort() releasing it and Start() re-claiming it.
                // Retry with a fresh port rather than let a TOCTOU race fail the test.
                lastError = ex;
                listener.Close();
            }
        }

        throw new InvalidOperationException(
            $"Could not bind a loopback HttpListener after {maxAttempts} attempts.", lastError);
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
