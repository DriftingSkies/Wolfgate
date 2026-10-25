#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client.Inventory;
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.RolledSleeves;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.RolledSleeves;

/// <summary>Any jumpsuit gets the roll verbs; each style cuts the worn layers or swaps in a drawn sprite.</summary>
[TestFixture]
public sealed class WFRolledSleevesTest : InteractionTest
{
    private const string MaskSuffix = "-wf-rolled-sleeves";

    [TestCase("ClothingUniformJumpsuitEngineering", null)]
    [TestCase("ClothingUniformJumpsuitColorGrey", "down")]
    [TestCase("ClothingUniformJumpsuitMilitaryCaptain", "down")]
    public async Task StylesRestyleTheWornLayers(string suitId, string? rolledDownMask)
    {
        var target = await SpawnTarget("MobHuman");
        var mob = ToServer(target);
        EntityUid suit = default;

        await Server.WaitAssertion(() =>
        {
            suit = SEntMan.SpawnEntity(suitId, SEntMan.GetCoordinates(TargetCoords));
            Assert.That(SEntMan.HasComponent<WFRolledSleevesComponent>(suit), Is.True,
                "The jumpsuit didn't get rollable sleeves.");
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, suit, "jumpsuit", force: true), Is.True);
        });
        await RunTicks(5);
        await AssertMasked(target, null);

        foreach (var (style, mask) in new (WFJumpsuitStyle, string?)[]
                 {
                     (WFJumpsuitStyle.SleevesRolled, "sleeves"),
                     (WFJumpsuitStyle.RolledDown, rolledDownMask),
                     (WFJumpsuitStyle.Normal, null),
                 })
        {
            await Server.WaitAssertion(() =>
            {
                var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
                SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), style, mob);
            });
            await RunTicks(5);
            await AssertMasked(target, mask);
        }

        if (rolledDownMask != null)
            return;

        // A suit with a drawn Meridian roll-down swaps to that sprite instead of the cut.
        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
            SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), WFJumpsuitStyle.RolledDown, mob);
        });
        await RunTicks(5);
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var drawn = slots.VisualLayerKeys["jumpsuit"]
                .Where(k => sprite.LayerMapTryGet(k, out _))
                .Select(k => ((SpriteComponent.Layer) sprite[sprite.LayerMapGet(k)]).ActualRsi?.Path.ToString())
                .Any(p => p != null && p.StartsWith("/Textures/_WF/RolledSleeves/Meridian/"));
            Assert.That(drawn, Is.True, "The rolled-down suit didn't use its drawn Meridian sprite.");
        });
    }

    /// <summary>
    /// A wearer the client first sees already dressed (an outfit spawn, or walking into view) still redraws when the
    /// sleeves are rolled.
    /// </summary>
    [Test]
    public async Task RollsOnSuitTheClientFirstSawWorn()
    {
        EntityUid mob = default;
        EntityUid suit = default;

        // Spawned and dressed in one tick, so the client never sees the equip happen.
        await Server.WaitAssertion(() =>
        {
            mob = SEntMan.SpawnEntity("MobHuman", SEntMan.GetCoordinates(TargetCoords));
            suit = SEntMan.SpawnEntity("ClothingUniformJumpsuitEngineering", SEntMan.GetCoordinates(TargetCoords));
            Assert.That(SEntMan.System<InventorySystem>().TryEquip(mob, suit, "jumpsuit", force: true), Is.True);
        });
        await RunTicks(5);
        var target = SEntMan.GetNetEntity(mob);
        await AssertMasked(target, null);

        await Server.WaitAssertion(() =>
        {
            var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
            SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), WFJumpsuitStyle.SleevesRolled, mob);
        });
        await RunTicks(5);
        await AssertMasked(target, "sleeves");
    }

    /// <summary>Every playable species gets the sleeve cut on a plain jumpsuit.</summary>
    [Test]
    public async Task EverySpeciesRollsSleeves()
    {
        var species = Server.ProtoMan.EnumeratePrototypes<SpeciesPrototype>()
            .Where(s => s.RoundStart)
            .OrderBy(s => s.ID)
            .ToList();
        var failures = new List<string>();

        foreach (var proto in species)
        {
            var target = await SpawnTarget(proto.Prototype);
            var mob = ToServer(target);
            var equipped = false;

            await Server.WaitAssertion(() =>
            {
                var suit = SEntMan.SpawnEntity("ClothingUniformJumpsuitDetective", SEntMan.GetCoordinates(TargetCoords));
                equipped = SEntMan.System<InventorySystem>().TryEquip(mob, suit, "jumpsuit", force: true);
                if (equipped)
                {
                    var comp = SEntMan.GetComponent<WFRolledSleevesComponent>(suit);
                    SEntMan.System<WFRolledSleevesSystem>().SetStyle((suit, comp), WFJumpsuitStyle.SleevesRolled, mob);
                }
            });

            if (!equipped)
            {
                failures.Add($"{proto.ID}: can't wear a jumpsuit");
                continue;
            }

            await RunTicks(5);
            await Client.WaitAssertion(() =>
            {
                var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
                var keys = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target)).VisualLayerKeys["jumpsuit"];
                if (keys.Any(k => k.EndsWith(MaskSuffix)))
                    return;

                var states = keys.Where(k => sprite.LayerMapTryGet(k, out _))
                    .Select(k => (SpriteComponent.Layer) sprite[sprite.LayerMapGet(k)])
                    .Select(l => $"{l.ActualRsi?.Path}:{l.State.Name}");
                failures.Add($"{proto.ID}: no mask ({string.Join(", ", states)})");
            });
        }

        Assert.That(failures, Is.Empty, string.Join("\n", failures));
    }

    private async Task AssertMasked(NetEntity target, string? maskState)
    {
        await Client.WaitAssertion(() =>
        {
            var sprite = CEntMan.GetComponent<SpriteComponent>(ToClient(target));
            var slots = CEntMan.GetComponent<InventorySlotsComponent>(ToClient(target));
            var maskKeys = slots.VisualLayerKeys["jumpsuit"].Where(k => k.EndsWith(MaskSuffix)).ToList();
            if (maskState == null)
            {
                Assert.That(maskKeys, Is.Empty, "A suit worn this way still has a mask layer.");
                return;
            }

            Assert.That(maskKeys, Is.Not.Empty, "A restyled suit has no mask layer.");
            foreach (var maskKey in maskKeys)
            {
                var suitKey = maskKey[..^MaskSuffix.Length];
                Assert.That(sprite.LayerMapTryGet(suitKey, out var suitIndex), Is.True);
                Assert.That(sprite.LayerMapTryGet(maskKey, out var maskIndex), Is.True);
                Assert.That(maskIndex, Is.LessThan(suitIndex), "The mask layer must come before the suit layer.");
                Assert.That(((SpriteComponent.Layer) sprite[maskIndex]).State.Name, Is.EqualTo(maskState));

                var shader = ((SpriteComponent.Layer) sprite[suitIndex]).ShaderPrototype?.Id;
                Assert.That(shader, Is.AnyOf("WFRolledSleeves", "WFRolledSleevesDisplaced"));
            }
        });
    }
}
