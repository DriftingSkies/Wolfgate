using Content.Shared._WG.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WG.RoundSetup;

/// <summary>Faction recruitment follows actual jobs, not titles, foreign jobs or unknown ranks.</summary>
[TestFixture]
public sealed class WGFactionCrewPolicyTest
{
    [TestCase(WGCrewFaction.None, WGShipStartLocation.RandomSpace, true)]
    [TestCase(WGCrewFaction.None, WGShipStartLocation.CentralDock, true)]
    [TestCase(WGCrewFaction.None, WGShipStartLocation.FactionBase, false)]
    [TestCase(WGCrewFaction.TSF, WGShipStartLocation.RandomSpace, true)]
    [TestCase(WGCrewFaction.TSF, WGShipStartLocation.FactionBase, true)]
    [TestCase(WGCrewFaction.TSF, WGShipStartLocation.CentralDock, false)]
    [TestCase(WGCrewFaction.PDV, WGShipStartLocation.RandomSpace, true)]
    [TestCase(WGCrewFaction.PDV, WGShipStartLocation.FactionBase, true)]
    [TestCase(WGCrewFaction.PDV, WGShipStartLocation.CentralDock, false)]
    [TestCase(WGCrewFaction.VG, WGShipStartLocation.FactionBase, true)]
    [TestCase(WGCrewFaction.USSP, WGShipStartLocation.RandomSpace, true)]
    [TestCase(WGCrewFaction.Underground, WGShipStartLocation.CentralDock, false)]
    [TestCase((WGCrewFaction) 255, WGShipStartLocation.RandomSpace, false)]
    [TestCase(WGCrewFaction.TSF, (WGShipStartLocation) 255, false)]
    public void DeparturesStayWithinTheirFaction(WGCrewFaction faction, WGShipStartLocation location, bool allowed)
    {
        Assert.That(WGRoundSetupPolicy.IsShipStartAllowed(location, faction), Is.EqualTo(allowed));
    }

    [TestCase(WGCrewFaction.TSF, "Bailiff", "SeniorOfficer", true)]
    [TestCase(WGCrewFaction.TSF, "Bailiff", "Bailiff", true)]
    [TestCase(WGCrewFaction.TSF, "Bailiff", "Sheriff", false)]
    [TestCase(WGCrewFaction.TSF, "Deputy", "Brigmedic", true)]
    [TestCase(WGCrewFaction.TSF, "Deputy", "Pirate", false)]
    [TestCase(WGCrewFaction.PDV, "PirateFirstMate", "PDVDenasvar", true)]
    [TestCase(WGCrewFaction.PDV, "PirateFirstMate", "PirateCaptain", false)]
    [TestCase(WGCrewFaction.PDV, "PirateCaptain", "PDVPrince", false)]
    [TestCase(WGCrewFaction.VG, "VGCommander", "VGLieutenant", true)]
    [TestCase(WGCrewFaction.VG, "VGLieutenant", "VGCommander", false)]
    [TestCase(WGCrewFaction.USSP, "USSPSergeant", "USSPRifleman", true)]
    [TestCase(WGCrewFaction.USSP, "USSPSergeant", "VGInfanteer", false)]
    [TestCase(WGCrewFaction.Underground, "Godfather", "Fugitive", true)]
    [TestCase(WGCrewFaction.Underground, "Fugitive", "Godfather", false)]
    [TestCase(WGCrewFaction.None, "Sheriff", "Deputy", false)]
    [TestCase(WGCrewFaction.TSF, null, "Deputy", false)]
    [TestCase(WGCrewFaction.TSF, "Sheriff", null, false)]
    public void RecruitmentRequiresKnownRanks(WGCrewFaction faction, string host, string recruit, bool allowed)
    {
        Assert.That(WGFactionCrewPolicy.CanRecruit(faction, host, recruit), Is.EqualTo(allowed));
    }
}
