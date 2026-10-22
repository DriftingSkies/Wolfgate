using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._WF.MappingMode;
using NUnit.Framework;

namespace Content.Tests._WF.MappingMode;

[TestFixture]
public sealed class WFMappingSessionCodesTest
{
    [Test]
    public void GeneratedCodesUseTheAlphabet()
    {
        var random = new Random(1234);
        for (var i = 0; i < 200; i++)
        {
            var code = WFMappingSessionCodes.Generate(random.Next, new HashSet<string>());
            Assert.Multiple(() =>
            {
                Assert.That(code, Has.Length.EqualTo(WFMappingSessionCodes.Length));
                Assert.That(code.All(c => WFMappingSessionCodes.Alphabet.Contains(c)), code);
                Assert.That(code.IndexOfAny(new[] { 'I', 'O', '0', '1' }), Is.EqualTo(-1), code);
                Assert.That(WFMappingSessionCodes.IsValid(code), code);
            });
        }
    }

    [Test]
    public void TakenCodesAreSkipped()
    {
        // The first six draws spell AAAAAA, the next six BBBBBB.
        var draws = 0;
        int Next(int max) => draws++ < WFMappingSessionCodes.Length ? 0 : 1;

        var code = WFMappingSessionCodes.Generate(Next, new HashSet<string> { "AAAAAA" });
        Assert.That(code, Is.EqualTo("BBBBBB"));
    }

    [TestCase(" abc234 ", "ABC234")]
    [TestCase("xyz789", "XYZ789")]
    public void CodesAreNormalized(string typed, string expected)
    {
        Assert.That(WFMappingSessionCodes.Normalize(typed), Is.EqualTo(expected));
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
        Assert.That(WFMappingSessionCodes.IsValid(code), Is.EqualTo(valid));
    }
}
