using System.Collections.Generic;
using System.Linq;
using Content.Server._Common.Consent;
using Content.Server._WG.Genitals;
using Content.Server.Database;
using Content.Shared._WG.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Moq;
using NUnit.Framework;
using Robust.Shared.Log;

namespace Content.Tests._WG.Prototypes;

/// <summary>Data saved under a renamed prototype id loads under the current id.</summary>
[TestFixture]
public sealed class WGLegacyPrototypeIdsTest
{
    [Test]
    public void ResolveTest()
    {
        var table = WGLegacyPrototypeIds.ConsentToggles;

        Assert.Multiple(() =>
        {
            Assert.That(WGLegacyPrototypeIds.Resolve(table, "UndergarmentStrip"), Is.EqualTo("WGUndergarmentStrip"));
            Assert.That(WGLegacyPrototypeIds.Resolve(table, "WGUndergarmentStrip"), Is.EqualTo("WGUndergarmentStrip"));
            Assert.That(WGLegacyPrototypeIds.Resolve(table, "GenitalMarkings"), Is.EqualTo("GenitalMarkings"));
            Assert.That(WGLegacyPrototypeIds.Resolve(table, null), Is.Null);
        });
    }

    /// <summary>A consent row saved under an old toggle id reads as the current toggle.</summary>
    [Test]
    public void ConsentRowTest()
    {
        var db = new ConsentSettings
        {
            ConsentFreetext = "",
            ConsentToggles = new List<ConsentToggle>
            {
                new() { ToggleProtoId = "UndergarmentStrip", ToggleProtoState = "on" },
                new() { ToggleProtoId = "WGAnatomySurgery", ToggleProtoState = "on" },
                new() { ToggleProtoId = "GenitalMarkings", ToggleProtoState = "on" },
            },
        };

        var ids = db.ToPlayerConsentSettings().Toggles.Keys.Select(k => k.Id);
        Assert.That(ids, Is.EquivalentTo(new[] { "WGUndergarmentStrip", "WGAnatomySurgery", "GenitalMarkings" }));
    }

    /// <summary>An anatomy column saved with old shape ids loads with the current ones.</summary>
    [Test]
    public void GenitalShapeColumnTest()
    {
        var log = new Mock<ISawmill>().Object;
        var profile = GenitalProfileJson.Deserialize(
            "{\"v\":1,\"penis\":{\"shape\":\"GenitalShapePenisKnotted\"},\"vagina\":{\"shape\":\"GenitalShapeVaginaHuman\"},"
            + "\"breasts\":{\"shape\":\"WGGenitalShapeBreastsPair\"}}",
            log, 1);

        Assert.That(profile, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(profile!.LoadFailed, Is.False);
            Assert.That(profile.Penis?.Shape.Id, Is.EqualTo("WGGenitalShapePenisKnotted"));
            Assert.That(profile.Vagina?.Shape.Id, Is.EqualTo("WGGenitalShapeVaginaHuman"));
            Assert.That(profile.Breasts?.Shape.Id, Is.EqualTo("WGGenitalShapeBreastsPair"));
        });
    }

    /// <summary>A profile saved with an old species and loadout id loads with the current ones.</summary>
    [Test]
    public void ProfileTest()
    {
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Canine");
        var role = new RoleLoadout("JobContractor");
        role.SelectedLoadouts["ContractorFirearm"] = new List<Loadout>
        {
            new() { Prototype = "ContractorMosinLoadout" },
            new() { Prototype = "ContractorMk32Loadout" },
        };
        profile.SetLoadout(role);

        WGLegacyPrototypeIds.ResolveProfile(profile);

        var loadouts = profile.Loadouts["JobContractor"].SelectedLoadouts["ContractorFirearm"].Select(l => l.Prototype.Id);
        Assert.Multiple(() =>
        {
            Assert.That(profile.Species.Id, Is.EqualTo("WGCanine"));
            Assert.That(loadouts, Is.EqualTo(new[] { "WGContractorMosinLoadout", "ContractorMk32Loadout" }));
        });
    }
}
