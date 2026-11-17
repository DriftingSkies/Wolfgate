#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server.Database;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.ShipCommand;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Inventory;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.ShipCommand;

/// <summary>
/// Personal presets: a Lupercal's settings saved to the database come back unchanged and set up a freshly spawned
/// Lupercal, whose officer, listed by player alone, is recognised in a new body with a new card. Spawn Vessel's
/// default preset path applies the first of its players' presets that exists.
/// </summary>
[TestFixture]
[TestOf(typeof(WGShipPresetSystem))]
public sealed class WGShipPresetTest
{
    [Test]
    public async Task PresetRoundTripOntoAFreshShip()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var db = server.ResolveDependency<IServerDbManager>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var command = entMan.System<WGShipCommandSystem>();
        var presets = entMan.System<WGShipPresetSystem>();
        var roster = entMan.System<WGShipRosterSystem>();
        var shipJobs = entMan.System<WGShipJobsSystem>();
        var session = pair.Player!;
        var map = await pair.CreateTestMap();

        EntityUid first = default, second = default;
        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WGLupercal");
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(500, 0), null, out var a, createStation: true), Is.True);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(-500, 0), null, out var b, createStation: true), Is.True);
            first = a!.Value;
            second = b!.Value;
        });
        await pair.RunTicksSync(5);

        string json = string.Empty, zone = string.Empty, post = string.Empty;
        await server.WaitPost(() =>
        {
            Assert.That(presets.VesselOf(first), Is.EqualTo("WGLupercal"));
            var zones = entMan.GetComponent<WGShipZonesComponent>(first);
            zone = zones.Zones.First(z => z.Mode != WGShipZoneMode.Locked).Id;
            Assert.That(command.SetZoneMode((first, zones), zone, WGShipZoneMode.Locked), Is.True);
            var cmd = entMan.GetComponent<WGShipCommandComponent>(first);
            Assert.That(command.AddPerson((first, cmd), WGShipCommandList.Officers, null, session.UserId, "Tess Arden"), Is.True);
            cmd.AdminsServedFree = false;
            var crew = entMan.GetComponent<WGShipRosterComponent>(first);
            post = crew.Slots.Keys.OrderBy(k => k).First();
            Assert.That(roster.SetStoodDown((first, crew), post, true, null), Is.True);
            roster.SetAutoRestaff((first, crew), true, null);
            Assert.That(shipJobs.SetSlots((first, entMan.GetComponent<WGShipJobsComponent>(first)), "WGLupercalMarine", 2, null), Is.True);
            json = WGShipPresetSystem.Serialize(presets.Capture(first));
        });

        // Through the database and back.
        await db.SaveShipCommandPresetAsync(session.UserId.UserId, "WGLupercal", "test", json);
        var stored = await db.GetShipCommandPresetAsync(session.UserId.UserId, "WGLupercal", "test");
        Assert.That(stored, Is.EqualTo(json), "The preset comes back from the database unchanged.");
        var list = await db.GetShipCommandPresetsAsync(session.UserId.UserId, "WGLupercal");
        Assert.That(list.Select(p => p.Name), Is.EquivalentTo(new[] { "test" }));
        Assert.That(await db.GetShipCommandPresetAsync(new NetUserId(System.Guid.NewGuid()).UserId, "WGLupercal", "test"), Is.Null,
            "Presets are personal: another player sees none.");

        EntityUid body = default;
        await server.WaitPost(() =>
        {
            presets.Apply(second, WGShipPresetSystem.Deserialize(stored!)!, null, "test");

            // The officer was kept by player: a fresh body with a fresh card is still an officer on the fresh ship.
            body = entMan.SpawnEntity("MobHuman", new EntityCoordinates(second, Vector2.Zero));
            var card = entMan.SpawnEntity("PassengerIDCard", new EntityCoordinates(second, Vector2.Zero));
            entMan.EnsureComponent<StationRecordKeyStorageComponent>(card);
            entMan.System<StationRecordKeyStorageSystem>().AssignKey(card, new StationRecordKey(42, second));
            Assert.That(entMan.System<InventorySystem>().TryEquip(body, card, "id", force: true), Is.True);
            entMan.System<Content.Server.Mind.MindSystem>().ControlMob(session.UserId, body);
            // An admin would outrank the officer list.
            var admins = server.ResolveDependency<Content.Server.Administration.Managers.IAdminManager>();
            if (admins.IsAdmin(session))
                admins.DeAdmin(session);
        });

        await server.WaitAssertion(() =>
        {
            var zones = entMan.GetComponent<WGShipZonesComponent>(second);
            var cmd = entMan.GetComponent<WGShipCommandComponent>(second);
            var crew = entMan.GetComponent<WGShipRosterComponent>(second);
            var jobs = entMan.GetComponent<WGShipJobsComponent>(second);
            Assert.Multiple(() =>
            {
                Assert.That(zones.Zones.Single(z => z.Id == zone).Mode, Is.EqualTo(WGShipZoneMode.Locked));
                Assert.That(cmd.Officers.Select(p => p.User), Is.EquivalentTo(new NetUserId?[] { session.UserId }));
                Assert.That(cmd.AdminsServedFree, Is.False);
                Assert.That(crew.AutoRestaff, Is.True);
                Assert.That(crew.Slots[post].StoodDown, Is.True, "The stood-down post is stood down on the fresh ship.");
                Assert.That(shipJobs.GetSlots((second, jobs))["WGLupercalMarine"], Is.EqualTo(2));
                Assert.That(command.GetAuthority(body, second), Is.EqualTo(WGShipAuthority.Officer),
                    "A player listed by account is an officer in a new body with a new card.");
            });
        });

        // Spawn Vessel's path: the first listed player with a preset of the name is the one applied.
        await db.SaveShipCommandPresetAsync(session.UserId.UserId, "WGLupercal", WGShipPresetSystem.DefaultName, json);
        Task<NetUserId?> load = default!;
        await server.WaitPost(() =>
            load = presets.LoadFirst(first, new[] { new NetUserId(System.Guid.NewGuid()), session.UserId }, WGShipPresetSystem.DefaultName, null));
        for (var i = 0; i < 50 && !load.IsCompleted; i++)
            await pair.RunTicksSync(5);
        Assert.That(load.IsCompleted, Is.True);
        Assert.That(await load, Is.EqualTo((NetUserId?) session.UserId), "The owner's default preset is applied when the admin has none.");

        await db.DeleteShipCommandPresetAsync(session.UserId.UserId, "WGLupercal", "test");
        await db.DeleteShipCommandPresetAsync(session.UserId.UserId, "WGLupercal", WGShipPresetSystem.DefaultName);
        await pair.CleanReturnAsync();
    }
}
