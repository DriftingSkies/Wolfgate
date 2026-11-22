#nullable enable
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Research.Prototypes;
using Content.Shared.Xenoarchaeology.Artifact.XAT.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Prototype and map data reports from the 2026-11-21 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1121DataTest
{
    /// <summary>The block of a map entity: its "- uid:" line up to the next entity or prototype header.</summary>
    private static string EntityBlock(string map, string uid)
    {
        var match = Regex.Match(map, @"^  - uid: " + uid + @"\r?\n(?:(?!  - uid: |- proto: ).*\r?\n?)*", RegexOptions.Multiline);
        Assert.That(match.Success, Is.True, $"entity {uid} not found");
        return match.Value;
    }

    /// <summary>#1259: the Caracara's air mixer is set to 21% oxygen, 79% nitrogen, not the 50/50 default.</summary>
    [Test]
    public async Task CaracaraMixerIsAir()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var map = resources.ContentFileReadText(new ResPath("/SharedMaps/_Mono/Shuttles/Civilian/caracara.yml")).ReadToEnd();
            var mixer = EntityBlock(map, "293");
            Assert.That(mixer, Does.Contain("inletOneConcentration: 0.21"));
            Assert.That(mixer, Does.Contain("inletTwoConcentration: 0.79"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1266: the Wolf Snake's plasma windoor faces west, toward the shield generator it shuts in.</summary>
    [Test]
    public async Task WolfSnakeWindoorFacesShieldGenerator()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var resources = server.ResolveDependency<IResourceManager>();

        await server.WaitAssertion(() =>
        {
            var map = resources.ContentFileReadText(new ResPath("/SharedMaps/_Mono/Shuttles/VG/wolfsnake.yml")).ReadToEnd();
            Assert.That(EntityBlock(map, "1117"), Does.Contain("rot: -1.5707963267948966 rad"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1172: a turbine's pipe stubs are not saved with a ship, so a resold ship does not reload duplicates.</summary>
    [Test]
    public async Task TurbinePipeStubsAreNotSaved()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            Assert.That(protos.Index<EntityPrototype>("TurbineGasPipe").MapSavable, Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1262: the 8x65mm SKR ammunition box is unlocked by some technology.</summary>
    [Test]
    public async Task SkrAmmoBoxIsResearchable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var unlocked = protos.EnumeratePrototypes<TechnologyPrototype>()
                .Any(tech => tech.RecipeUnlocks.Contains("AmmoBox8x65mmSKR"));
            Assert.That(unlocked, Is.True);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>#1199: the artefact blood node reacts to Shadekin and Avali blood too.</summary>
    [Test]
    public async Task BloodNodeReactsToShadekinAndAvaliBlood()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var protos = server.ProtoMan;

        await server.WaitAssertion(() =>
        {
            var reactive = protos.Index<EntityPrototype>("TriggerBlood").Components["XATReactive"].Component as XATReactiveComponent;
            Assert.That(reactive, Is.Not.Null);
            Assert.That(reactive!.Reagents, Does.Contain((ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>) "DarkBlood"));
            Assert.That(reactive.Reagents, Does.Contain((ProtoId<Content.Shared.Chemistry.Reagent.ReagentPrototype>) "AvaliBlood"));
        });

        await pair.CleanReturnAsync();
    }
}
