using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Content.Client._WF.MameArcade;
using NUnit.Framework;

namespace Content.Tests._WF.MameArcade;

/// <summary>The sandbox-safe zip reader finds entries by CRC and size and inflates them.</summary>
[TestFixture]
[TestOf(typeof(MameZip))]
public sealed class MameZipTest
{
    [Test]
    public void ReadsStoredAndDeflatedEntries()
    {
        var stored = Enumerable.Range(0, 3000).Select(i => (byte) i).ToArray();
        var deflated = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("NEO GEO ", 4000)));
        using var zip = Build(("kof94/055-p1.p1", stored, CompressionLevel.NoCompression), ("055-s1.s1", deflated, CompressionLevel.Optimal));

        var entries = MameZip.ReadDirectory(zip);
        Assert.That(entries.Select(e => e.Name), Is.EqualTo(new[] { "kof94/055-p1.p1", "055-s1.s1" }));
        Assert.That(entries[0].Crc, Is.EqualTo(MameZip.Crc(stored)));
        Assert.That(entries[1].Size, Is.EqualTo(deflated.Length));
        Assert.That(entries[1].CompressedSize, Is.LessThan(deflated.Length), "The second entry is compressed");

        Assert.That(MameZip.Read(zip, entries[0]), Is.EqualTo(stored));
        Assert.That(MameZip.Read(zip, entries[1]), Is.EqualTo(deflated));
    }

    [Test]
    public void RejectsDamage()
    {
        var data = Enumerable.Range(0, 512).Select(i => (byte) (i * 3)).ToArray();
        using var zip = Build(("chip.bin", data, CompressionLevel.NoCompression));
        var entry = MameZip.ReadDirectory(zip).Single();

        // Flip a byte of the stored data, just past its local header.
        zip.Position = entry.LocalHeader + 30 + "chip.bin".Length + 10;
        zip.WriteByte(0xff);

        Assert.Throws<InvalidDataException>(() => MameZip.Read(zip, entry));
        Assert.Throws<InvalidDataException>(() => MameZip.ReadDirectory(new MemoryStream(new byte[64])));
    }

    [Test]
    public void CrcMatchesTheStandard()
    {
        Assert.That(MameZip.Crc(Encoding.ASCII.GetBytes("123456789")), Is.EqualTo(0xcbf43926));
    }

    private static MemoryStream Build(params (string Name, byte[] Data, CompressionLevel Level)[] files)
    {
        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        {
            foreach (var (name, data, level) in files)
            {
                using var stream = archive.CreateEntry(name, level).Open();
                stream.Write(data, 0, data.Length);
            }
        }

        memory.Position = 0;
        return memory;
    }
}
