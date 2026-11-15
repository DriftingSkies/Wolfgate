#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WF.Photography;
using Content.Shared._Onyx.Body.Systems;
using Content.Shared._WF.Wolfmed.Body;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared._Shitmed.Medical.Surgery.Conditions;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared.Roles;
using Content.Shared.Station;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Reports from the 2026-11-14 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1114Test
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: startingGear
  id: WFTestBagKeysGear
  storage:
    back:
    - EncryptionKeyService
    - EncryptionKeySecurity
";

    /// <summary>
    /// Reports 860/939/813: paying for loadout items at spawn swaps the cached profile for one with the new balance, and
    /// the album lookup by reference then found no slot, so no scrapbook was given.
    /// </summary>
    [Test]
    public void AlbumFindsTheSlotAfterLoadoutPayment()
    {
        var spawned = HumanoidCharacterProfile.DefaultWithSpecies().WithName("Aqen");
        var paid = spawned.WithBankBalance(spawned.BankBalance - 900);
        var other = HumanoidCharacterProfile.DefaultWithSpecies().WithName("Someone Else");
        var prefs = new PlayerPreferences(new[]
        {
            new KeyValuePair<int, ICharacterProfile>(0, other),
            new KeyValuePair<int, ICharacterProfile>(3, paid),
        }, 3, default);

        Assert.That(prefs.TryIndexOfCharacter(spawned, out _), Is.False, "The reference lookup should miss here.");
        Assert.That(PhotoAlbumSystem.TryFindSlot(prefs, spawned, out var slot), Is.True,
            "The spawned profile's slot wasn't found after its balance changed.");
        Assert.That(slot, Is.EqualTo(3));
    }

    /// <summary>
    /// Report 940: a judge wearing a back item with no storage lost the bag items of their starting gear, both radio
    /// keys among them, so their headset had only Common.
    /// </summary>
    [Test]
    public async Task StartingBagItemsSurviveABackSlotWithoutStorage()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = em.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = em.System<InventorySystem>();
            var jetpack = em.SpawnEntity("JetpackBlue", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, jetpack, "back", silent: true, force: true), "The jetpack wasn't worn.");
            var gear = server.ProtoMan.Index<StartingGearPrototype>("WFTestBagKeysGear");
            em.System<SharedStationSpawningSystem>().EquipStartingGear(mob, gear, raiseEvent: false);

            var held = em.System<SharedHandsSystem>().EnumerateHeld(mob)
                .Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID)
                .ToList();
            Assert.That(held, Is.SupersetOf(new[] { "EncryptionKeyService", "EncryptionKeySecurity" }),
                "The starting bag items didn't reach the mob.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 940: with the starting-gear event raised, the keys that landed in hand go into the worn headset.</summary>
    [Test]
    public async Task KeysFromHandsAreSlottedIntoTheHeadset()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var mob = em.SpawnEntity("MobHuman", map.GridCoords);
            var inventory = em.System<InventorySystem>();
            Assert.That(inventory.TryEquip(mob, em.SpawnEntity("JetpackBlue", map.GridCoords), "back", silent: true, force: true));
            var headset = em.SpawnEntity("ClothingHeadsetGrey", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, headset, "ears", silent: true, force: true), "The headset wasn't worn.");
            var gear = server.ProtoMan.Index<StartingGearPrototype>("WFTestBagKeysGear");
            em.System<SharedStationSpawningSystem>().EquipStartingGear(mob, gear, raiseEvent: true);

            var keys = em.GetComponent<EncryptionKeyHolderComponent>(headset).KeyContainer.ContainedEntities
                .Select(key => em.GetComponent<MetaDataComponent>(key).EntityPrototype?.ID)
                .ToList();
            Assert.That(keys, Is.SupersetOf(new[] { "EncryptionKeyService", "EncryptionKeySecurity" }),
                "The keys from hand weren't slotted into the headset.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 903: a failed organ is kept at 0 health for the heal surgery, which then refused to list it, so the organ
    /// could only be replaced.
    /// </summary>
    [Test]
    public async Task FailedOrganListsItsHealSurgery()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default, torso = default;

        await server.WaitAssertion(() =>
        {
            body = em.SpawnEntity("MobHuman", map.GridCoords);
            var graph = em.System<SharedBodySystem>();
            torso = graph.GetBodyChildren(body).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var lungs = graph.GetPartOrgans(torso)
                .Single(organ => em.GetComponent<MetaDataComponent>(organ.Id).EntityPrototype?.ID == "OrganHumanLungs").Id;
            em.System<OrganHealthSystem>().SetHealth((lungs, em.GetComponent<WolfmedOrganComponent>(lungs)), FixedPoint2.Zero);
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var singleton = em.System<SharedSurgerySystem>().GetSingleton("WFSurgeryHealLungs");
            Assert.That(singleton, Is.Not.Null);
            var ev = new SurgeryValidEvent(body, torso);
            em.EventBus.RaiseLocalEvent(singleton!.Value, ref ev);
            Assert.That(ev.Cancelled, Is.False, "Heal Lungs isn't offered for failed lungs.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 871: a wall light's fixture on the floor tile it faces blocked anchoring a machine there.</summary>
    [Test]
    public async Task WallLightDoesNotBlockAnchoring()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            em.SpawnEntity("PoweredlightEmpty", map.GridCoords);
            var grid = (map.Grid.Owner, em.GetComponent<MapGridComponent>(map.Grid.Owner));
            var tile = em.System<SharedMapSystem>().TileIndicesFor(grid.Owner, grid.Item2, map.GridCoords);
            foreach (var id in new[] { "MachineFTLDrive", "SMESBasic" })
            {
                var machine = em.SpawnEntity(id, map.GridCoords.Offset(new System.Numerics.Vector2(3, 0)));
                var body = em.GetComponent<PhysicsComponent>(machine);
                Assert.That(em.System<AnchorableSystem>().TileFree(grid, tile, body.CollisionLayer, body.CollisionMask, wfAnchoring: true),
                    $"A wall light blocks anchoring {id} under it.");
                Assert.That(em.System<AnchorableSystem>().TileFree(grid, tile, body.CollisionLayer, body.CollisionMask), Is.False,
                    "Dungeon placement must still treat a wall light's tile as taken.");
            }
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 713: blood splashed on a lab floor touched loose grey extracts into monkey cubes, which nearby showers
    /// rehydrated, a monkey farm that ran itself. Only injected blood makes cubes.
    /// </summary>
    [Test]
    public async Task SpilledBloodMakesNoMonkeyCubes()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid extract = default;

        await server.WaitPost(() =>
        {
            extract = em.SpawnEntity("GreySlimeExtract", map.GridCoords);
            em.System<ReactiveSystem>().DoEntityReaction(extract, new Solution("Blood", FixedPoint2.New(5)), ReactionMethod.Touch);
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(extract), Is.False, "Spilled blood used up the extract.");
            Assert.That(em.EntityQuery<MetaDataComponent>().Count(m => m.EntityPrototype?.ID == "MonkeyCube"), Is.Zero,
                "Spilled blood turned a grey extract into monkey cubes.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Report 835: a synthetic biotype gets its battery at runtime, and the client's copy never learned it was battery
    /// powered, so it predicted full speed while the server slowed it on low charge: rubberbanding.
    /// </summary>
    [Test]
    public async Task RuntimeSiliconBatteryReachesTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        NetEntity net = default;

        await server.WaitPost(() =>
        {
            var mob = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            var silicon = server.EntMan.AddComponent<Content.Shared._EinsteinEngines.Silicon.Components.SiliconComponent>(mob);
            silicon.BatteryPowered = true;
            silicon.SpeedModifierThresholds = new Dictionary<int, float> { { 4, 1f }, { 1, 0.45f } };
            server.EntMan.Dirty(mob, silicon);
            net = server.EntMan.GetNetEntity(mob);
        });
        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            var mob = pair.Client.EntMan.GetEntity(net);
            var silicon = pair.Client.EntMan.GetComponent<Content.Shared._EinsteinEngines.Silicon.Components.SiliconComponent>(mob);
            Assert.That(silicon.BatteryPowered, "The client doesn't know the body runs on a battery.");
            Assert.That(silicon.SpeedModifierThresholds, Is.Not.Null.And.ContainKey(1), "The client has no low-charge speeds.");
        });
        await pair.CleanReturnAsync();
    }
}
