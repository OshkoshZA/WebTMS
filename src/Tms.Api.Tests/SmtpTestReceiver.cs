using System.Buffers;
using System.Net;
using System.Net.Sockets;
using MimeKit;
using SmtpServer;
using SmtpServer.Protocol;
using SmtpServer.Storage;

namespace Tms.Api.Tests;

/// <summary>
/// Stands in for a real mail relay — a real local SMTP server (the SmtpServer package,
/// not a mock) on an ephemeral loopback port, so LoadConfirmationEmailTests can assert
/// on the actual message SmtpEmailSender composed and sent, not a mocked stand-in for
/// it. The same "real local listener" approach WebhookTestReceiver already established
/// for webhooks, applied to SMTP instead. One instance per EmailTestFixture (the whole
/// test class shares it, unlike WebhookTestReceiver's per-test instances), since the
/// port is baked into that fixture's own WebApplicationFactory config at construction
/// time and can't change per test the way a webhook subscription's callback URL can.
/// </summary>
public sealed class SmtpTestReceiver : IDisposable
{
    private readonly SmtpServer.SmtpServer _server;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<MimeMessage> _messages = new();

    public int Port { get; }
    public IReadOnlyList<MimeMessage> Messages { get { lock (_messages) return _messages.ToList(); } }

    public SmtpTestReceiver()
    {
        Port = GetFreeLoopbackPort();

        var options = new SmtpServerOptionsBuilder()
            .ServerName("localhost")
            .Port(Port, false)
            .Build();

        var serviceProvider = new SmtpServer.ComponentModel.ServiceProvider();
        serviceProvider.Add(new CapturingMessageStore(_messages));

        _server = new SmtpServer.SmtpServer(options, serviceProvider);
        _ = _server.StartAsync(_cts.Token);
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _server.Shutdown();
        _cts.Cancel();
        _cts.Dispose();
    }

    private sealed class CapturingMessageStore : MessageStore
    {
        private readonly List<MimeMessage> _messages;

        public CapturingMessageStore(List<MimeMessage> messages) => _messages = messages;

        public override async Task<SmtpResponse> SaveAsync(
            ISessionContext context, IMessageTransaction transaction, ReadOnlySequence<byte> buffer, CancellationToken cancellationToken)
        {
            using var stream = new MemoryStream();
            foreach (var segment in buffer)
                await stream.WriteAsync(segment, cancellationToken);
            stream.Position = 0;

            var message = await MimeMessage.LoadAsync(stream, cancellationToken);
            lock (_messages) _messages.Add(message);

            return SmtpResponse.Ok;
        }
    }
}
