using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._WG.Headshot;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Server._WG.Headshot;

/// <summary>A fetched headshot as PNG bytes and their hash, or the loc id of why it failed.</summary>
public sealed record HeadshotResult(string? Hash, byte[]? Png, string? Error)
{
    public static HeadshotResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// Downloads headshot images and re-encodes them as small PNGs. Connections only go to public addresses, checked
/// at connect time, so a URL can't reach the server's own network, even through DNS rebinding.
/// Redirects aren't followed, so the fetched host is always the one checked against the allowlist.
/// </summary>
public sealed class HeadshotFetcher : IDisposable
{
    private const int MaxSourceDimension = 4096;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;
    private readonly ISawmill _sawmill;

    public HeadshotFetcher(ISawmill sawmill)
    {
        _sawmill = sawmill;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            UseCookies = false,
            AllowAutoRedirect = false,
            ConnectCallback = ConnectPublicAsync,
        };
        _http = new HttpClient(handler) { Timeout = Timeout };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Wolfgate-Headshot/1.0");
    }

    public async Task<HeadshotResult> FetchAsync(Uri uri, long maxBytes)
    {
        byte[]? data;
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
                return HeadshotResult.Fail("wg-headshot-error-download");

            if (response.Content.Headers.ContentLength > maxBytes)
                return HeadshotResult.Fail("wg-headshot-error-too-large");

            await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
            data = await ReadLimitedAsync(stream, maxBytes, cts.Token);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        {
            _sawmill.Debug($"Headshot download from {uri.Host} failed: {e.Message}");
            return HeadshotResult.Fail("wg-headshot-error-download");
        }

        if (data == null)
            return HeadshotResult.Fail("wg-headshot-error-too-large");

        return await Task.Run(() => Process(data));
    }

    /// <summary>Scales the image to fit <see cref="HeadshotRules.ImageSize"/> and re-encodes it as PNG.</summary>
    public static HeadshotResult Process(byte[] data)
    {
        try
        {
            var info = Image.Identify(data);
            if (info.Width is <= 0 or > MaxSourceDimension || info.Height is <= 0 or > MaxSourceDimension)
                return HeadshotResult.Fail("wg-headshot-error-dimensions");

            var size = new Size(HeadshotRules.ImageSize, HeadshotRules.ImageSize);
            using var image = Image.Load<Rgba32>(new DecoderOptions { MaxFrames = 1 }, data);
            image.Mutate(x => x.Resize(new ResizeOptions { Size = size, Mode = ResizeMode.Max }));

            using var output = new MemoryStream();
            image.SaveAsPng(output);
            var png = output.ToArray();
            return new HeadshotResult(Convert.ToHexString(SHA256.HashData(png)), png, null);
        }
        catch (Exception e) when (e is ImageFormatException or NotSupportedException)
        {
            return HeadshotResult.Fail("wg-headshot-error-format");
        }
    }

    /// <summary>Whether an address is on the public internet, not loopback, private, link-local or reserved.</summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(b[0] is 0 or 10 or 127 or >= 224
                     || b[0] == 100 && (b[1] & 0xC0) == 64 // 100.64.0.0/10, carrier-grade NAT
                     || b[0] == 169 && b[1] == 254
                     || b[0] == 172 && (b[1] & 0xF0) == 16
                     || b[0] == 192 && b[1] == 168
                     || b[0] == 192 && b[1] == 0 && b[2] == 0
                     || b[0] == 198 && (b[1] & 0xFE) == 18);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        return !(IPAddress.IsLoopback(address)
                 || address.Equals(IPAddress.IPv6Any)
                 || address.IsIPv6LinkLocal
                 || address.IsIPv6SiteLocal
                 || address.IsIPv6Multicast
                 || (b[0] & 0xFE) == 0xFC // fc00::/7, unique local
                 || b[0] == 0x20 && b[1] == 0x02 // 2002::/16, 6to4 can wrap a private IPv4
                 || b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B); // 64:ff9b::/96, NAT64 likewise
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        foreach (var address in addresses)
        {
            if (!IsPublic(address))
                continue;

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose();
            }
        }

        throw new HttpRequestException($"No reachable public address for {context.DnsEndPoint.Host}.");
    }

    /// <summary>Reads the whole stream, or returns null once it passes <paramref name="maxBytes"/>.</summary>
    private static async Task<byte[]?> ReadLimitedAsync(Stream stream, long maxBytes, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, token)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
