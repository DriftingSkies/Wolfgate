using Content.Server._WG.Prototypes;
using NUnit.Framework;

namespace Content.Tests._WG.Prototypes;

/// <summary>A map saved under the old WF prefix reads under WG, with its tile data untouched.</summary>
[TestFixture]
public sealed class WGLegacyMapTextTest
{
    [Test]
    public void ConvertTest()
    {
        const string old =
            "tilemap:\r\n" +
            "  3: WFPlatingDurasteel\r\n" +
            "entities:\r\n" +
            "- proto: WFWallDurasteel\r\n" +
            "  entities:\r\n" +
            "  - uid: 2\r\n" +
            "    components:\r\n" +
            "    - type: WFShipAlert\r\n" +
            "      code: WFShipCodeRed\r\n" +
            "      sprite: /Textures/_WF/ShipPa/alert.rsi\r\n" +
            "      rule: !type:WFDrydockRule\r\n" +
            "      note: an UNLAWFUL but awful wfcollapse\r\n" +
            "    tiles: AwAA/WFAAAA+wf-AAAAWF==\r\n";

        const string current =
            "tilemap:\r\n" +
            "  3: WGPlatingDurasteel\r\n" +
            "entities:\r\n" +
            "- proto: WGWallDurasteel\r\n" +
            "  entities:\r\n" +
            "  - uid: 2\r\n" +
            "    components:\r\n" +
            "    - type: WGShipAlert\r\n" +
            "      code: WGShipCodeRed\r\n" +
            "      sprite: /Textures/_WG/ShipPa/alert.rsi\r\n" +
            "      rule: !type:WGDrydockRule\r\n" +
            "      note: an UNLAWFUL but awful wfcollapse\r\n" +
            "    tiles: AwAA/WFAAAA+wf-AAAAWF==\r\n";

        Assert.Multiple(() =>
        {
            Assert.That(WGLegacyMapText.Convert(old), Is.EqualTo(current));
            Assert.That(WGLegacyMapText.Convert(current), Is.SameAs(current));
        });
    }
}
