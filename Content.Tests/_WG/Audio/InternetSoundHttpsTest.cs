using System.Threading;
using Content.Server._WG.Audio.InternetSound;
using NUnit.Framework;

namespace Content.Tests._WG.Audio;

[TestFixture]
public sealed class InternetSoundHttpsTest
{
    [TestCase("http://example.com/audio")]
    [TestCase("http://example.com:443/audio?token=secret")]
    [TestCase("http://user:password@example.com/audio")]
    [TestCase("https://example.com:80/audio")]
    [TestCase("https://example.com:8443/audio")]
    [TestCase("file:///tmp/audio.ogg")]
    public void InsecureLinksAreRejectedBeforeStartingDownloader(string url)
    {
        var settings = new InternetSoundDownloader.Settings("missing-yt-dlp", "missing-ffmpeg",
            420, 30, 10, 22050, 1, 32);
        var error = Assert.ThrowsAsync<InternetSoundDownloader.FetchException>(async () =>
            await InternetSoundDownloader.Fetch(url, settings, CancellationToken.None));
        Assert.That(error!.LocKey, Is.EqualTo("wg-internet-sound-error-https"));
    }
}
