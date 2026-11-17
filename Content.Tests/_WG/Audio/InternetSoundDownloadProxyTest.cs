#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._WG.Audio.InternetSound;
using NUnit.Framework;

namespace Content.Tests._WG.Audio;

[TestFixture]
public sealed class InternetSoundDownloadProxyTest
{
    [TestCase("127.0.0.1")]
    [TestCase("10.1.2.3")]
    [TestCase("172.16.1.1")]
    [TestCase("192.168.1.1")]
    [TestCase("169.254.169.254")]
    [TestCase("168.63.129.16")]
    [TestCase("100.100.100.200")]
    [TestCase("0.0.0.0")]
    [TestCase("224.0.0.1")]
    [TestCase("198.18.0.1")]
    [TestCase("::1")]
    [TestCase("::ffff:127.0.0.1")]
    [TestCase("fc00::1")]
    [TestCase("fe80::1")]
    [TestCase("64:ff9b::a00:1")]
    [TestCase("2002:7f00:1::")]
    [TestCase("2001:db8::1")]
    public void PrivateAndSpecialAddressesAreBlocked(string address) =>
        Assert.That(InternetSoundDownloadProxy.IsNonPublic(IPAddress.Parse(address)), Is.True);

    [TestCase("8.8.8.8")]
    [TestCase("1.1.1.1")]
    [TestCase("2606:4700:4700::1111")]
    public void PublicAddressesAreAllowed(string address) =>
        Assert.That(InternetSoundDownloadProxy.IsNonPublic(IPAddress.Parse(address)), Is.False);

    [Test]
    public async Task RebindingIsRecheckedAndConnectionsUseTheValidatedIp()
    {
        var resolutions = 0;
        var connections = 0;
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse(++resolutions == 1 ? "8.8.8.8" : "127.0.0.1") }),
            (address, port, _) =>
            {
                Assert.That(address, Is.EqualTo(IPAddress.Parse("8.8.8.8")));
                Assert.That(port, Is.EqualTo(443));
                Interlocked.Increment(ref connections);
                connected.TrySetResult();
                return Task.FromResult<Stream>(new MemoryStream());
            });
        Assert.That(await Request(proxy.Url, "rebind.test", 443, timeout.Token, ClientHelloPrefix), Is.Zero);
        await connected.Task.WaitAsync(timeout.Token);
        Assert.That(await Request(proxy.Url, "rebind.test", 443, timeout.Token), Is.EqualTo(2));
        Assert.That(Volatile.Read(ref connections), Is.EqualTo(1));
    }

    [Test]
    public async Task MixedDnsAnswersAndNonWebPortsAreRejectedBeforeConnecting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connections = 0;
        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Loopback }),
            (_, _, _) => { connections++; return Task.FromResult<Stream>(new MemoryStream()); });
        Assert.That(await Request(proxy.Url, "mixed.test", 443, timeout.Token), Is.EqualTo(2));
        Assert.That(await Request(proxy.Url, "8.8.8.8", 22, timeout.Token), Is.EqualTo(2));
        Assert.That(connections, Is.Zero);
    }

    [Test]
    public async Task HttpsFetchAndRedirectsStayOnValidatedTlsTransport()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var origin = new TlsServer(request => request.Contains("GET /private ", StringComparison.Ordinal)
            ? Response("302 Found", location: "https://private.test/audio")
            : request.Contains("GET /downgrade ", StringComparison.Ordinal)
                ? Response("302 Found", location: "http://public.test:80/audio")
                : request.Contains("GET /downgrade443 ", StringComparison.Ordinal)
                    ? Response("302 Found", location: "http://public.test:443/audio?token=secret")
                : Response("200 OK", "fixture audio"));
        var connections = 0;
        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            (host, _) => Task.FromResult(new[] { host == "public.test" ? IPAddress.Parse("8.8.8.8") : IPAddress.Loopback }),
            async (_, _, token) =>
            {
                Interlocked.Increment(ref connections);
                // Only the test connector maps the approved public address to a local fixture.
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(IPAddress.Loopback, origin.Port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            });
        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(proxy.Url),
            UseProxy = true,
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                certificate is not null
                && certificate.GetCertHashString() == origin.Certificate.GetCertHashString()
        };
        using var http = new HttpClient(handler);
        Assert.That(await http.GetStringAsync("https://public.test/audio", timeout.Token), Is.EqualTo("fixture audio"));

        using (var privateRedirect = await http.GetAsync("https://public.test/private", timeout.Token))
        {
            Assert.That(privateRedirect.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            Assert.That(privateRedirect.Headers.Location?.ToString(), Is.EqualTo("https://private.test/audio"));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await http.GetAsync(privateRedirect.Headers.Location!, timeout.Token));
        }

        using (var downgrade = await http.GetAsync("https://public.test/downgrade", timeout.Token))
        {
            Assert.That(downgrade.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            Assert.That(downgrade.Headers.Location?.ToString(), Is.EqualTo("http://public.test/audio"));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await http.GetAsync(downgrade.Headers.Location!, timeout.Token));
        }

        using (var downgrade = await http.GetAsync("https://public.test/downgrade443", timeout.Token))
        {
            Assert.That(downgrade.StatusCode, Is.EqualTo(HttpStatusCode.Found));
            Assert.ThrowsAsync<HttpRequestException>(async () =>
                await http.GetAsync(downgrade.Headers.Location!, timeout.Token));
        }

        Assert.That(proxy.DeniedHost, Is.EqualTo("private.test"));
        Assert.That(proxy.RejectedInsecureTransport, Is.True);
        Assert.That(Volatile.Read(ref connections), Is.EqualTo(4));
        Assert.That(origin.Connections, Is.EqualTo(4));
        Assert.That(origin.Requests, Is.EqualTo(4));
    }

    [Test]
    public async Task DirectHttpAndHttpOnPort443AreRejectedBeforeConnecting()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var connections = 0;
        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _, _) =>
            {
                Interlocked.Increment(ref connections);
                return Task.FromResult<Stream>(new MemoryStream());
            });

        Assert.That(await Request(proxy.Url, "public.test", 80, timeout.Token), Is.EqualTo(2));
        var httpRequest = Encoding.ASCII.GetBytes("GET /audio HTTP/1.1\r\nHost: public.test\r\n\r\n");
        Assert.That(await Request(proxy.Url, "public.test", 443, timeout.Token, httpRequest, waitForClose: true), Is.Zero);
        Assert.That(proxy.RejectedInsecureTransport, Is.True);
        Assert.That(Volatile.Read(ref connections), Is.Zero);
    }

    [Test]
    public async Task FragmentedInputSharesOneAtomicCapAcrossTunnelsAndCancelsDownload()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var cancelled = 0;
        await using var proxy = new InternetSoundDownloadProxy(timeout.Token,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("8.8.8.8") }),
            (_, _, _) => Task.FromResult<Stream>(new FragmentedStream(new byte[6], 3)),
            maxDownloadBytes: 8,
            onDownloadLimitExceeded: () => Interlocked.Exchange(ref cancelled, 1));

        var reads = await Task.WhenAll(
            ReadThroughProxy(proxy.Url, "one.test", timeout.Token),
            ReadThroughProxy(proxy.Url, "two.test", timeout.Token));

        Assert.That(reads[0].Length + reads[1].Length, Is.LessThanOrEqualTo(8));
        Assert.That(reads[0].Length + reads[1].Length, Is.GreaterThan(0));
        Assert.That(proxy.DownloadLimitExceeded, Is.True);
        Assert.That(Volatile.Read(ref cancelled), Is.EqualTo(1));
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposingClosesAnIncompleteHandshake(bool beginTls)
    {
        var proxy = new InternetSoundDownloadProxy(CancellationToken.None);
        using var client = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, new Uri(proxy.Url).Port, timeout.Token);
            if (beginTls)
            {
                var stream = client.GetStream();
                await stream.WriteAsync(new byte[] { 5, 1, 0 }, timeout.Token);
                await stream.ReadExactlyAsync(new byte[2], timeout.Token);
                await stream.WriteAsync(new byte[] { 5, 1, 0, 1, 8, 8, 8, 8, 1, 187 }, timeout.Token);
                var reply = new byte[10];
                await stream.ReadExactlyAsync(reply, timeout.Token);
                Assert.That(reply[1], Is.Zero);
                await stream.WriteAsync(new byte[] { 22, 3 }, timeout.Token);
            }
        }
        finally
        {
            await proxy.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private static async Task<byte> Request(string proxyUrl, string host, int port, CancellationToken token,
        byte[]? afterReply = null, bool waitForClose = false)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, new Uri(proxyUrl).Port, token);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, token);
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, token);
        var name = Encoding.ASCII.GetBytes(host);
        using var request = new MemoryStream();
        request.Write(new byte[] { 5, 1, 0, 3, (byte) name.Length });
        request.Write(name);
        request.Write(new[] { (byte) (port >> 8), (byte) port });
        await stream.WriteAsync(request.ToArray(), token);
        var reply = new byte[10];
        await stream.ReadExactlyAsync(reply, token);
        if (reply[1] == 0 && afterReply is not null)
        {
            await stream.WriteAsync(afterReply, token);
            if (waitForClose)
            {
                try
                {
                    Assert.That(await stream.ReadAsync(new byte[1], token), Is.Zero);
                }
                catch (IOException)
                {
                    // A rejected tunnel may close with a reset instead of a clean EOF.
                }
            }
        }
        return reply[1];
    }

    private static async Task<byte[]> ReadThroughProxy(string proxyUrl, string host, CancellationToken token)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, new Uri(proxyUrl).Port, token);
        var stream = client.GetStream();
        await stream.WriteAsync(new byte[] { 5, 1, 0 }, token);
        var greeting = new byte[2];
        await stream.ReadExactlyAsync(greeting, token);
        var name = Encoding.ASCII.GetBytes(host);
        using var request = new MemoryStream();
        request.Write(new byte[] { 5, 1, 0, 3, (byte) name.Length });
        request.Write(name);
        request.Write(new byte[] { 1, 187 });
        await stream.WriteAsync(request.ToArray(), token);
        var reply = new byte[10];
        await stream.ReadExactlyAsync(reply, token);
        if (reply[1] != 0)
            return Array.Empty<byte>();

        await stream.WriteAsync(ClientHelloPrefix, token);
        using var result = new MemoryStream();
        var buffer = new byte[32];
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read == 0)
                    return result.ToArray();
                result.Write(buffer, 0, read);
            }
        }
        catch (IOException)
        {
            return result.ToArray();
        }
    }

    private static readonly byte[] ClientHelloPrefix = { 22, 3, 3, 0, 4, 1 };

    private sealed class FragmentedStream(byte[] data, int fragmentSize) : MemoryStream()
    {
        private readonly byte[] _data = data;
        private int _offset;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_offset == _data.Length)
                return 0;
            var read = Math.Min(Math.Min(count, fragmentSize), _data.Length - _offset);
            Array.Copy(_data, _offset, buffer, offset, read);
            _offset += read;
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, fragmentSize), _data.Length - _offset);
            _data.AsMemory(_offset, count).CopyTo(buffer);
            _offset += count;
            return ValueTask.FromResult(count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private static byte[] Response(string status, string body = "", string? location = null)
    {
        var locationHeader = location is null ? "" : $"Location: {location}\r\n";
        return Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status}\r\n{locationHeader}Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}");
    }

    private sealed class TlsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly X509Certificate2 _certificate = CreateCertificate();
        private readonly Func<string, byte[]> _response;
        private readonly CancellationTokenSource _stop = new();
        private readonly object _gate = new();
        private readonly List<TcpClient> _clients = new();
        private readonly List<Task> _tasks = new();
        private readonly Task _accept;
        private int _connections;
        private int _requests;

        public TlsServer(Func<string, byte[]> response)
        {
            _response = response;
            _listener.Start();
            _accept = Accept();
        }

        public int Port => ((IPEndPoint) _listener.LocalEndpoint).Port;
        public X509Certificate2 Certificate => _certificate;
        public int Connections => Volatile.Read(ref _connections);
        public int Requests => Volatile.Read(ref _requests);

        private async Task Accept()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_gate)
                    {
                        _clients.Add(client);
                        _tasks.Add(Serve(client));
                    }
                    Interlocked.Increment(ref _connections);
                }
            }
            catch (Exception e) when (e is OperationCanceledException or SocketException or ObjectDisposedException)
            {
            }
        }

        private async Task Serve(TcpClient client)
        {
            using (client)
            using (var stream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false))
            {
                try
                {
                    await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                    }, _stop.Token);
                    var request = await ReadRequest(stream, _stop.Token);
                    Interlocked.Increment(ref _requests);
                    await stream.WriteAsync(_response(request), _stop.Token);
                    await stream.FlushAsync(_stop.Token);
                }
                catch (Exception e) when (e is IOException or SocketException or OperationCanceledException
                                          or ObjectDisposedException or AuthenticationException)
                {
                }
            }
        }

        private static async Task<string> ReadRequest(Stream stream, CancellationToken token)
        {
            using var request = new MemoryStream();
            var buffer = new byte[1024];
            while (request.Length < 16 * 1024)
            {
                var read = await stream.ReadAsync(buffer, token);
                if (read == 0)
                    break;
                request.Write(buffer, 0, read);
                var bytes = request.ToArray();
                for (var i = Math.Max(0, bytes.Length - read - 3); i <= bytes.Length - 4; i++)
                {
                    if (bytes[i] == '\r' && bytes[i + 1] == '\n' && bytes[i + 2] == '\r' && bytes[i + 3] == '\n')
                        return Encoding.ASCII.GetString(bytes);
                }
            }

            throw new IOException("The fixture did not receive a complete HTTP request.");
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _accept.WaitAsync(TimeSpan.FromSeconds(2));
            TcpClient[] clients;
            Task[] tasks;
            lock (_gate)
            {
                clients = _clients.ToArray();
                tasks = _tasks.ToArray();
            }

            foreach (var client in clients)
                client.Dispose();

            try
            {
                await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2));
            }
            finally
            {
                _certificate.Dispose();
                _stop.Dispose();
            }
        }

        private static X509Certificate2 CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=fixture.test", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));
            // Schannel needs an imported private key rather than the ephemeral RSA handle.
            return X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
        }
    }
}
