using Content.Shared._WF.RoundSetup;
using NUnit.Framework;

namespace Content.Tests._WF.RoundSetup;

/// <summary>Faction recruitment follows actual jobs, not titles, foreign jobs or unknown ranks.</summary>
[TestFixture]
public sealed class WFFactionCrewPolicyTest
{
    [TestCase(WFCrewFaction.None, WFShipStartLocation.RandomSpace, true)]
    [TestCase(WFCrewFaction.None, WFShipStartLocation.CentralDock, true)]
    [TestCase(WFCrewFaction.None, WFShipStartLocation.FactionBase, false)]
    [TestCase(WFCrewFaction.TSF, WFShipStartLocation.RandomSpace, true)]
    [TestCase(WFCrewFaction.TSF, WFShipStartLocation.FactionBase, true)]
    [TestCase(WFCrewFaction.TSF, WFShipStartLocation.CentralDock, false)]
    [TestCase(WFCrewFaction.PDV, WFShipStartLocation.RandomSpace, true)]
    [TestCase(WFCrewFaction.PDV, WFShipStartLocation.FactionBase, true)]
    [TestCase(WFCrewFaction.PDV, WFShipStartLocation.CentralDock, false)]
    [TestCase(WFCrewFaction.VG, WFShipStartLocation.FactionBase, true)]
    [TestCase(WFCrewFaction.USSP, WFShipStartLocation.RandomSpace, true)]
    [TestCase(WFCrewFaction.Underground, WFShipStartLocation.CentralDock, false)]
    [TestCase((WFCrewFaction) 255, WFShipStartLocation.RandomSpace, false)]
    [TestCase(WFCrewFaction.TSF, (WFShipStartLocation) 255, false)]
    public void DeparturesStayWithinTheirFaction(WFCrewFaction faction, WFShipStartLocation location, bool allowed)
    {
        Assert.That(WFRoundSetupPolicy.IsShipStartAllowed(location, faction), Is.EqualTo(allowed));
    }

    [TestCase(WFCrewFaction.TSF, "Bailiff", "SeniorOfficer", true)]
    [TestCase(WFCrewFaction.TSF, "Bailiff", "Bailiff", true)]
    [TestCase(WFCrewFaction.TSF, "Bailiff", "Sheriff", false)]
    [TestCase(WFCrewFaction.TSF, "Deputy", "Brigmedic", true)]
    [TestCase(WFCrewFaction.TSF, "Deputy", "Pirate", false)]
    [TestCase(WFCrewFaction.PDV, "PirateFirstMate", "PDVDenasvar", true)]
    [TestCase(WFCrewFaction.PDV, "PirateFirstMate", "PirateCaptain", false)]
    [TestCase(WFCrewFaction.PDV, "PirateCaptain", "PDVPrince", false)]
    [TestCase(WFCrewFaction.VG, "VGCommander", "VGLieutenant", true)]
    [TestCase(WFCrewFaction.VG, "VGLieutenant", "VGCommander", false)]
    [TestCase(WFCrewFaction.USSP, "USSPSergeant", "USSPRifleman", true)]
    [TestCase(WFCrewFaction.USSP, "USSPSergeant", "VGInfanteer", false)]
    [TestCase(WFCrewFaction.Underground, "Godfather", "Fugitive", true)]
    [TestCase(WFCrewFaction.Underground, "Fugitive", "Godfather", false)]
    [TestCase(WFCrewFaction.None, "Sheriff", "Deputy", false)]
    [TestCase(WFCrewFaction.TSF, null, "Deputy", false)]
    [TestCase(WFCrewFaction.TSF, "Sheriff", null, false)]
    public void RecruitmentRequiresKnownRanks(WFCrewFaction faction, string host, string recruit, bool allowed)
    {
        Assert.That(WFFactionCrewPolicy.CanRecruit(faction, host, recruit), Is.EqualTo(allowed));
    }
}
