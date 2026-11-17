#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Species;

/// <summary>
/// Clothing with explicit clothingVisuals layers draws its Avali states on an Avali instead of the human states
/// under a displacement map. A human control keeps the human states.
/// </summary>
[TestFixture]
public sealed class AvaliClothingTest : InteractionTest
{
    /// <summary>A hardsuit helmet whose head layers (shell, unshaded and light) all have Avali states.</summary>
    private const string Helmet = "ClothingHeadHelmetHardsuitAtmos";

    [Test]
    public async Task ExplicitLayersUseSpeciesStatesTest()
    {
        var (avaliStates, avaliKeys) = await EquippedHeadStates("MobAvali");
        Assert.Multiple(() =>
        {
            Assert.That(avaliStates, Is.Not.Empty, "The helmet added no head layers.");
            Assert.That(avaliStates, Has.All.EndsWith("-avali"), "An Avali got a human helmet state.");
            Assert.That(avaliKeys, Has.None.EndsWith("-displacement"), "An Avali helmet layer got a displacement map.");
        });

        var (humanStates, _) = await EquippedHeadStates("MobHuman");
        Assert.That(humanStates, Has.None.EndsWith("-avali"), "A human got an Avali helmet state.");
    }

    /// <summary>Spawns a mob, equips the helmet on the server and reads the client's head layer states.</summary>
    private async Task<(List<string> States, List<string> Keys)> EquippedHeadStates(string mob)
    {
        var target = await SpawnTarget(mob);
        var helmet = await SpawnEntity(Helmet, SEntMan.GetCoordinates(TargetCoords));
        var sTarget = ToServer(target);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(sTarget, helmet, "head", force: true), Is.True,
                $"Could not equip the helmet on {mob}.");
        });
        await RunTicks(10);

        var states = new List<string>();
        var revealed = new List<string>();
        var cTarget = ToClient(target);
        await Client.WaitAssertion(() =>
        {
            var spriteUid = cTarget;
            var sprites = CEntMan.System<SpriteSystem>();
            var sprite = CEntMan.GetComponent<SpriteComponent>(spriteUid);
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(cTarget);
            Assert.That(slots.VisualLayerKeys.TryGetValue("head", out var headKeys), Is.True, "No head layers were rendered.");
            foreach (var key in headKeys!)
            {
                revealed.Add(key);
                if (sprites.LayerMapTryGet((spriteUid, sprite), key, out var index, false))
                    states.Add(sprite[index].RsiState.Name ?? "");
            }
        });
        return (states, revealed);
    }
}
