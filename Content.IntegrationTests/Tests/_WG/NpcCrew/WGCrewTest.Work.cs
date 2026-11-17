#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.NPC.HTN;
using Content.Shared._WG.NpcCrew;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Repairable;
using Content.Shared._Mono.CCVar;
using Robust.Shared.Configuration;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Tools.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>Low-air crew use a real spare and refuse further work when no usable reserve remains.</summary>
    [Test]
    public async Task CrewReplacesLowOxygenTank()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var position = new EntityCoordinates(deck, new Vector2(2.5f));
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand, position, "eva")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var inventory = Server.System<InventorySystem>();
            Assert.That(inventory.TryGetSlotEntity(crew, "suitstorage", out var old), Is.True);
            var oldGas = SEntMan.GetComponent<GasTankComponent>(old!.Value);
            oldGas.Air.Clear();
            oldGas.Air.SetMoles(Gas.Oxygen, 0.01f);
            var spare = SEntMan.SpawnAtPosition("OxygenTankFilled", position);
            var eva = Server.System<WGCrewEvaSystem>();
            Assert.That(eva.Prepare(crew), Is.True);
            Assert.That(inventory.TryGetSlotEntity(crew, "suitstorage", out var worn), Is.True);
            Assert.That(worn, Is.EqualTo(spare));
            Assert.That(SEntMan.EntityExists(old.Value), Is.True, "The old tank is not consumed or refilled.");
            Assert.That(oldGas.Air.Pressure < 300, Is.True);
            SEntMan.GetComponent<GasTankComponent>(spare).Air.Clear();
            Assert.That(eva.Prepare(crew), Is.False, "No infinite oxygen or empty-tank EVA.");
        });
    }

    /// <summary>The built-in SRD restores missing snapshot tiles and structures through its normal do-after.</summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task CrewRebuildsSnapshot(bool structure)
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
            EntityUid crew = default;
            var work = Server.System<WGCrewWorkSystem>();
            await Server.WaitAssertion(() =>
            {
                var wall = structure ? SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(3.5f, 2.5f))) : (EntityUid?) null;
                crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                    new EntityCoordinates(deck, new Vector2(2.5f)), "rebuild")!.Value;
                SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
                if (wall is { } entity)
                    SEntMan.DeleteEntity(entity);
                else
                    Server.System<SharedMapSystem>().SetTile(deck,
                        SEntMan.GetComponent<Robust.Shared.Map.Components.MapGridComponent>(deck), new Vector2i(3, 2), new Tile(0));
                Assert.That(work.Advance(deck, "rebuild", WGCrewObjectiveKind.Repair, default), Is.EqualTo("working"));
            });
            await WaitUntil(() =>
            {
                work.Perform(crew);
                return !work.Destination(crew, out _);
            }, 900, () => "The built-in SRD should restore the missing part of the ship.");
            await Server.WaitAssertion(() => Assert.That(work.Advance(deck, "rebuild", WGCrewObjectiveKind.Repair, default), Is.EqualTo("complete")));
            await RunTicks(90);
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }

    /// <summary>The built-in repair tool completes ordinary timed repairs and is disabled on death.</summary>
    [Test]
    public async Task CrewRepairsWithRealTool()
    {
        var config = Client.ResolveDependency<IConfigurationManager>();
        var echo = config.GetCVar(MonoCVars.AreaEchoEnabled);
        await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, false));
        try
        {
            var deck = await CreateDeck(new Vector2(6, 0), 9, gravity: true);
            EntityUid crew = default, target = default;
            var work = Server.System<WGCrewWorkSystem>();
            await Server.WaitAssertion(() =>
            {
                var position = new EntityCoordinates(deck, new Vector2(2.5f));
                crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand, position, "repair")!.Value;
                target = SEntMan.SpawnAtPosition("WallSolid", new EntityCoordinates(deck, new Vector2(6.5f, 2.5f)));
                SEntMan.EnsureComponent<RepairableComponent>(target);
                var damage = new DamageSpecifier();
                damage.DamageDict.Add("Blunt", 5);
                Server.System<DamageableSystem>().TryChangeDamage(target, damage, ignoreResistances: true);
                Assert.That(SEntMan.GetComponent<DamageableComponent>(target).TotalDamage > 0, Is.True);
                Assert.That(SEntMan.HasComponent<ShipRepairToolComponent>(crew), Is.True);
                Assert.That(Server.System<Content.Shared.Tools.Systems.SharedToolSystem>().HasQuality(crew, "Applicating"), Is.True,
                    $"Built-in qualities: {SEntMan.GetComponent<ToolComponent>(crew).Qualities}; target: {string.Join(',', SEntMan.GetComponent<RepairableComponent>(target).Qualities)}");
                Assert.That(work.Advance(deck, "repair", WGCrewObjectiveKind.Repair, default), Is.EqualTo("working"));
            });
            await WaitUntil(() => SEntMan.GetComponent<DamageableComponent>(target).TotalDamage == 0, 900,
                () => $"The crew should walk over and repair the wall. {Describe(crew)}");
            await Server.WaitAssertion(() =>
            {
                work.Perform(crew);
                Assert.That(work.Advance(deck, "repair", WGCrewObjectiveKind.Repair, default), Is.EqualTo("complete"));
                Server.System<MobStateSystem>().ChangeMobState(crew, MobState.Dead);
                Assert.That(SEntMan.HasComponent<ShipRepairToolComponent>(crew), Is.False);
                Assert.That(SEntMan.HasComponent<ToolComponent>(crew), Is.False);
                Assert.That(work.Destination(crew, out _), Is.False);
            });
            await RunTicks(90);
        }
        finally
        {
            await Client.WaitPost(() => config.SetCVar(MonoCVars.AreaEchoEnabled, echo));
        }
    }

    /// <summary>Cargo must be reached and carried home before a collection order completes.</summary>
    [TestCase(WGCrewObjectiveKind.Salvage, "SheetSteel1")]
    [TestCase(WGCrewObjectiveKind.Resupply, "Magazine45_ACPPistolFMJ")]
    public async Task CrewCarriesSuppliesHome(WGCrewObjectiveKind kind, string prototype)
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        var source = await CreateDeck(new Vector2(24, 0), 5, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var home = new EntityCoordinates(deck, new Vector2(2.5f));
            var pickup = new EntityCoordinates(source, new Vector2(2.5f));
            var crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand, home, "cargo")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            var item = SEntMan.SpawnAtPosition(prototype, pickup);
            var work = Server.System<WGCrewWorkSystem>();
            var hands = Server.System<SharedHandsSystem>();
            Assert.That(work.Advance(deck, "cargo", kind, source), Is.EqualTo("working"));
            work.Perform(crew);
            Assert.That(hands.IsHolding(crew, item, out _), Is.False, "A job must not remotely collect cargo.");
            Server.System<SharedTransformSystem>().SetCoordinates(crew, pickup);
            Assert.That(work.Perform(crew), Is.True);
            Assert.That(hands.IsHolding(crew, item, out _), Is.True);
            Assert.That(work.Destination(crew, out var destination), Is.True);
            Assert.That(destination.EntityId, Is.EqualTo(deck));
            Server.System<SharedTransformSystem>().SetCoordinates(crew, home);
            work.Perform(crew);
            Assert.That(hands.IsHolding(crew, item, out _), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(item).GridUid, Is.EqualTo(deck));
            Assert.That(work.Advance(deck, "cargo", kind, source), Is.EqualTo("complete"));
        });
    }

    /// <summary>A crew with no live threat holsters its weapon instead of continually racking it.</summary>
    [Test]
    public async Task CrewHolstersWhenNoLiveThreatRemains()
    {
        var deck = await CreateDeck(new Vector2(6, 0), 5, gravity: true);
        EntityUid crew = default;
        await Server.WaitAssertion(() =>
        {
            crew = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(deck, new Vector2(2.5f)), "cleanup")!.Value;
            SEntMan.GetComponent<HTNComponent>(crew).Enabled = false;
            Assert.That(Server.System<WGCrewWeaponSystem>().TryDraw(crew), Is.True);
        });
        await RunTicks(70);
        await Server.WaitAssertion(() => Assert.That(Server.System<WGCrewWeaponSystem>().IsDrawn(crew), Is.False));
    }
}
