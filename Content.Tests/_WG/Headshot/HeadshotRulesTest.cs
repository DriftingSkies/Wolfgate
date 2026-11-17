using System.IO;
using System.Net;
using Content.Server._WG.Headshot;
using Content.Shared._WG.Headshot;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Tests._WG.Headshot;

/// <summary>Headshot URL rules, the host allowlist, the public address filter and image processing.</summary>
[TestFixture]
[TestOf(typeof(HeadshotRules))]
[TestOf(typeof(HeadshotFetcher))]
public sealed class HeadshotRulesTest
{
    [TestCase("https://i.example.com/face.png", true)]
    [TestCase("https://example.com:8443/a/b.jpg?x=1", true)]
    [TestCase("http://example.com/face.png", false)]
    [TestCase("ftp://example.com/face.png", false)]
    [TestCase("https://user:pass@example.com/face.png", false)]
    [TestCase("https://127.0.0.1/face.png", false)]
    [TestCase("https://[::1]/face.png", false)]
    [TestCase("https://2130706433/face.png", false)]
    [TestCase("https://", false)]
    [TestCase("not a url", false)]
    [TestCase("", false)]
    public void UrlValidityTest(string url, bool valid)
    {
        Assert.That(HeadshotRules.IsValid(url, out _), Is.EqualTo(valid));
    }

    [Test]
    public void CleanTest()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HeadshotRules.Clean(null), Is.Empty);
            Assert.That(HeadshotRules.Clean("  https://example.com/a.png  "), Is.EqualTo("https://example.com/a.png"));
            Assert.That(HeadshotRules.Clean("https://example.com/" + new string('a', HeadshotRules.MaxUrlLength)), Is.Empty);
        });
    }

    [TestCase("i.imgur.com", "", true)]
    [TestCase("i.imgur.com", "imgur.com", true)]
    [TestCase("imgur.com", "imgur.com, cdn.example.org", true)]
    [TestCase("cdn.example.org", "imgur.com, cdn.example.org", true)]
    [TestCase("evilimgur.com", "imgur.com", false)]
    [TestCase("imgur.com.evil.net", "imgur.com", false)]
    public void HostAllowlistTest(string host, string allowed, bool expected)
    {
        Assert.That(HeadshotRules.IsHostAllowed(host, allowed), Is.EqualTo(expected));
    }

    [TestCase("93.184.216.34", true)]
    [TestCase("2606:2800:220:1:248:1893:25c8:1946", true)]
    [TestCase("127.0.0.1", false)]
    [TestCase("10.1.2.3", false)]
    [TestCase("172.16.0.1", false)]
    [TestCase("172.31.255.255", false)]
    [TestCase("172.32.0.1", true)]
    [TestCase("192.168.1.1", false)]
    [TestCase("169.254.169.254", false)]
    [TestCase("100.64.0.1", false)]
    [TestCase("0.0.0.0", false)]
    [TestCase("224.0.0.1", false)]
    [TestCase("::1", false)]
    [TestCase("::", false)]
    [TestCase("fe80::1", false)]
    [TestCase("fd00::1", false)]
    [TestCase("::ffff:10.0.0.1", false)]
    [TestCase("::ffff:93.184.216.34", true)]
    [TestCase("64:ff9b::a00:1", false)]
    [TestCase("2002:a00:1::", false)]
    public void PublicAddressTest(string address, bool expected)
    {
        Assert.That(HeadshotFetcher.IsPublic(IPAddress.Parse(address)), Is.EqualTo(expected));
    }

    [Test]
    public void ProcessScalesToFitTest()
    {
        using var source = new Image<Rgba32>(400, 200, new Rgba32(200, 100, 50));
        using var stream = new MemoryStream();
        source.SaveAsJpeg(stream);

        var result = HeadshotFetcher.Process(stream.ToArray());
        Assert.That(result.Error, Is.Null);
        Assert.That(result.Hash, Is.Not.Null);

        using var output = Image.Load<Rgba32>(result.Png!);
        Assert.Multiple(() =>
        {
            Assert.That(output.Width, Is.EqualTo(HeadshotRules.ImageSize));
            Assert.That(output.Height, Is.EqualTo(HeadshotRules.ImageSize / 2));
        });
    }

    [Test]
    public void ProcessRejectsTest()
    {
        using var huge = new Image<Rgba32>(5000, 10);
        using var stream = new MemoryStream();
        huge.SaveAsPng(stream);

        Assert.Multiple(() =>
        {
            Assert.That(HeadshotFetcher.Process("<html>not an image</html>"u8.ToArray()).Error,
                Is.EqualTo("wg-headshot-error-format"));
            Assert.That(HeadshotFetcher.Process(stream.ToArray()).Error, Is.EqualTo("wg-headshot-error-dimensions"));
        });
    }
}
