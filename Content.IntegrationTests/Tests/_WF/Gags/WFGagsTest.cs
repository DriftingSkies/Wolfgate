#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.Gags;
using Content.Shared._WF.Gags;
using Content.Shared.Clothing.Components;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Gags;

/// <summary>Recolourable items: their layers name real colour slots, and picked loadout colours reach the item.</summary>
[TestFixture]
public sealed class WFGagsTest
{
    private static readonly Color Picked = Color.FromHex("#3A7BD5");

    /// <summary>Every coloured worn and in-hand layer of a recolourable item names a colour the item has.</summary>
    [Test]
    public async Task LayersNameRealSlotsTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var factory = server.ResolveDependency<IComponentFactory>();
        var bad = new List<string>();
        var count = 0;

        await server.WaitPost(() =>
        {
            foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
            {
                if (proto.Abstract || !proto.TryGetComponent<WFGagsComponent>(out var gags, factory))
                    continue;

                count++;
                var states = new List<string?>();
                if (proto.TryGetComponent<ClothingComponent>(out var clothing, factory))
                    states.AddRange(clothing.ClothingVisuals.Values.SelectMany(l => l).Select(l => l.State));
                if (proto.TryGetComponent<ItemComponent>(out var item, factory))
                    states.AddRange(item.InhandVisuals.Values.SelectMany(l => l).Select(l => l.State));

                if (gags.Colors.Count is 0 or > WFGagsColors.MaxColors)
                    bad.Add($"{proto.ID}: {gags.Colors.Count} colours");

                foreach (var state in states)
                {
                    if (WFGagsColors.SlotOf(state) is { } slot && slot >= gags.Colors.Count)
                        bad.Add($"{proto.ID}: {state} with {gags.Colors.Count} colours");
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(0), "No recolourable items found.");
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Colours picked for a loadout are on the item it puts on, and a colour change is a profile change.</summary>
    [Test]
    public async Task LoadoutColorsReachTheItemTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var gagsSystem = entMan.System<SharedWFGagsSystem>();
        var spawning = entMan.System<SharedStationSpawningSystem>();
        var inventory = entMan.System<InventorySystem>();
        var map = await pair.CreateTestMap();

        List<Color>? worn = null;
        string? itemId = null;
        var equalBefore = false;
        var equalAfter = true;

        await server.WaitPost(() =>
        {
            var (loadoutProto, group, role) = FindColorableLoadout(server.ProtoMan, gagsSystem);
            itemId = gagsSystem.ColorableItem(loadoutProto);

            var loadout = new RoleLoadout(role.ID);
            loadout.SelectedLoadouts[group] = new List<Loadout> { new() { Prototype = loadoutProto.ID } };
            var copy = loadout.Clone();
            equalBefore = loadout.Equals(copy);

            loadout.SetWFGagsColors(group, loadoutProto.ID, new List<Color> { Picked.WithAlpha(0.2f) });
            equalAfter = loadout.Equals(copy);

            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            spawning.EquipRoleLoadout(mob, loadout, role);

            foreach (var slot in loadoutProto.Equipment.Keys)
            {
                if (inventory.TryGetSlotEntity(mob, slot, out var ent) &&
                    entMan.TryGetComponent<WFGagsComponent>(ent, out var gags))
                {
                    worn = gags.Colors.ToList();
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(equalBefore, Is.True, "A copy of a loadout should equal it.");
            Assert.That(equalAfter, Is.False, $"Recolouring {itemId} in a loadout should count as a change.");
            Assert.That(worn, Is.Not.Null, $"{itemId} was not put on or is not recolourable.");
            Assert.That(worn![0], Is.EqualTo(Picked), "The picked colour, made opaque, should be the item's first.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Every recolourable item's sprite layers take its colours on the client. Spawning them also has the client
    /// load each item's RSI and states, and a missing state is an error that fails the test.
    /// </summary>
    [Test]
    public async Task ClientTintsSpriteLayersTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var entMan = client.EntMan;
        var factory = client.ResolveDependency<IComponentFactory>();
        var bad = new List<string>();
        var count = 0;

        await client.WaitPost(() =>
        {
            var gagsSystem = entMan.System<WFGagsSystem>();
            var spriteSystem = entMan.System<SpriteSystem>();
            var protos = client.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && p.ID.StartsWith("WFMeridian") && p.TryGetComponent<ClothingComponent>(out _, factory))
                .ToList();

            foreach (var proto in protos)
            {
                var uid = entMan.SpawnEntity(proto.ID, MapCoordinates.Nullspace);
                count++;
                if (!entMan.TryGetComponent<WFGagsComponent>(uid, out var gags))
                {
                    entMan.DeleteEntity(uid);
                    continue;
                }

                var colors = gags.Colors.Select(_ => Picked).ToList();
                gagsSystem.SetColors((uid, gags), colors);

                var sprite = entMan.GetComponent<SpriteComponent>(uid);
                var index = 0;
                foreach (var layer in sprite.AllLayers)
                {
                    var state = spriteSystem.LayerGetRsiState((uid, sprite), index).Name;
                    if (WFGagsColors.SlotOf(state) != null && layer.Color != Picked)
                        bad.Add($"{proto.ID} layer {state} is {layer.Color.ToHex()}");
                    index++;
                }

                entMan.DeleteEntity(uid);
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(count, Is.GreaterThan(0), "No ported clothing found.");
            Assert.That(bad, Is.Empty, string.Join("\n", bad));
        });

        await pair.CleanReturnAsync();
    }

    private static (LoadoutPrototype, ProtoId<LoadoutGroupPrototype>, RoleLoadoutPrototype) FindColorableLoadout(
        IPrototypeManager proto,
        SharedWFGagsSystem gags)
    {
        foreach (var role in proto.EnumeratePrototypes<RoleLoadoutPrototype>().OrderBy(r => r.ID))
        {
            foreach (var groupId in role.Groups)
            {
                var group = proto.Index(groupId);
                foreach (var loadoutId in group.Loadouts)
                {
                    var loadout = proto.Index(loadoutId);
                    if (loadout.ID.StartsWith("WFMeridian") && gags.ColorableItem(loadout) != null)
                        return (loadout, groupId, role);
                }
            }
        }

        Assert.Fail("No role offers a recolourable loadout.");
        throw new InvalidOperationException();
    }
}
