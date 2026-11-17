using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WG.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WG.MappingMode;

[TestFixture]
public sealed class WGMappingSessionCodesTest
{
    [Test]
    public void GeneratedCodesUseTheAlphabet()
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var code = WGMappingSessionCodes.Generate(random.Next, new HashSet<string>());
            Assert.Multiple(() =>
            {
                Assert.That(code, Has.Length.EqualTo(WGMappingSessionCodes.Length));
                Assert.That(code.All(c => WGMappingSessionCodes.Alphabet.Contains(c)), code);
                Assert.That(code.IndexOfAny(new[] { 'I', 'O', '0', '1' }), Is.EqualTo(-1), code);
                Assert.That(WGMappingSessionCodes.IsValid(code), code);
            });
        }
    }

    [Test]
    public void TakenCodesAreSkipped()
    {
        // The first six draws spell AAAAAA, the next six BBBBBB.
        var draws = 0;
        int Next(int max) => draws++ < WGMappingSessionCodes.Length ? 0 : 1;

        var code = WGMappingSessionCodes.Generate(Next, new HashSet<string> { "AAAAAA" });
        Assert.That(code, Is.EqualTo("BBBBBB"));
    }

    [TestCase(" abc234 ", "ABC234")]
    [TestCase("xyz789", "XYZ789")]
    public void CodesAreNormalized(string typed, string expected)
    {
        Assert.That(WGMappingSessionCodes.Normalize(typed), Is.EqualTo(expected));
    }

    [TestCase("ABCDEF", true)]
    [TestCase("ABCDE", false)]
    [TestCase("ABCDEFG", false)]
    [TestCase("ABCDE1", false)]
    [TestCase("ABCDEO", false)]
    [TestCase("abcdef", false)]
    [TestCase("", false)]
    public void ValidCodes(string code, bool valid)
    {
        Assert.That(WGMappingSessionCodes.IsValid(code), Is.EqualTo(valid));
    }
}
