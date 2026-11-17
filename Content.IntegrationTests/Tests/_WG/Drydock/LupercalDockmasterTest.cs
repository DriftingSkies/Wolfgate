#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WG.ShipAccess;
using Content.Server._WG.Administration.Systems;
using Content.Server._WG.Drydock;
using Content.Server._WG.Hailing;
using Content.Server._WG.ShipCommand;
using Content.Server.Power.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WG.Drydock;
using Content.Shared._WG.Hailing;
using Content.Shared._WG.ShipAccess;
using Content.Shared._WG.ShipCommand;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Drydock;

/// <summary>
/// The Lupercal's dockmaster mans the desk beside his counter, answers a hail there, offers the drydock and books a berth
/// on the ring. The desk is a helm in a public lobby, so a visitor can't open it and an officer can; and with him gone
/// someone at the desk can still offer a caller the drydock window.
/// </summary>
[TestFixture]
[TestOf(typeof(HailResponderSystem))]
public sealed class LupercalDockmasterTest
{
    private const string Desk = "WGComputerLupercalDockDesk";

    [Test]
    public async Task DockmasterTakesHailsAtHisDesk()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var hailing = server.System<HailingSystem>();
        var ui = server.System<SharedUserInterfaceSystem>();
        var map = await pair.CreateTestMap();

        EntityUid grid = default, desk = default, dockmaster = default, helm = default, pilot = default, officer = default;
        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WGLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
        });
        await pair.RunTicksSync(5);

        await server.WaitPost(() =>
        {
            desk = Single(em, grid, Desk);
            dockmaster = Single(em, grid, "WGLupercalDockmaster");
            SetPowered(em, desk);

            DrydockTest.SetFloor(pair, map.Grid, 3);
            helm = em.SpawnEntity("ComputerShuttle", new EntityCoordinates(map.Grid, new Vector2(1.5f, 0.5f)));
            SetPowered(em, helm);
            pilot = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, new Vector2(0.5f, 0.5f)));
            ui.OpenUi(helm, ShuttleConsoleUiKey.Key, pilot);
        });
        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<HailResponderComponent>(dockmaster).Console, Is.EqualTo(desk), "He mans the desk beside his counter.");

            var hands = server.System<SharedHandsSystem>();
            var (visitor, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Random Stranger", 3);
            (officer, var officerCard) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Ben Ortiz", 2);
            Assert.That(server.System<WGShipAccessSystem>().TryGetKey(officerCard, out var officerKey), Is.True);
            Assert.That(server.System<WGShipCommandSystem>().AddPerson((grid, em.GetComponent<WGShipCommandComponent>(grid)),
                WGShipCommandList.Officers, officerKey, null, "Ben Ortiz"), Is.True);

            var refused = new ActivatableUIOpenAttemptEvent(visitor);
            em.EventBus.RaiseLocalEvent(desk, refused);
            Assert.That(refused.Cancelled, Is.True, "A visitor can't open the desk's helm.");

            var allowed = new ActivatableUIOpenAttemptEvent(officer);
            em.EventBus.RaiseLocalEvent(desk, allowed);
            Assert.That(allowed.Cancelled, Is.False, "An officer can.");
            em.DeleteEntity(visitor);

            hailing.ClearCooldowns(helm, grid);
            Assert.That(hailing.TryHail(helm, pilot, grid), Is.True, "The hail should go out.");
        });

        bool Connected() => em.TryGetComponent(helm, out HailConsoleComponent? hail) && hail.State == HailState.Connected;
        await DrydockTest.RunUntil(pair, 80, 10, Connected);
        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(Connected(), Is.True, "The dockmaster should answer within his delay.");
            var hail = em.GetComponent<HailConsoleComponent>(helm);
            Assert.That(hail.Peer, Is.EqualTo(desk), "The call runs from his desk.");
            Assert.That(hail.Options.Select(o => o.Text), Does.Contain(Loc.GetString("drydock-hail-service")), "He offers the drydock.");

            var drydock = server.System<WGDrydockSystem>();
            var ship = DrydockTest.CreateDamagedShip(pair, map.MapId, new Vector2(120, 90));
            var payer = DrydockTest.CreatePayer(pair, map.MapId);
            drydock.Book(desk, em.GetComponent<WGDrydockOperatorComponent>(desk), ship, null, payer, null);

            EntityUid? booked = null;
            var bookings = em.EntityQueryEnumerator<WGDrydockBookingComponent, TransformComponent>();
            while (bookings.MoveNext(out var uid, out var booking, out var xform))
            {
                if (booking.Ship == ship && xform.GridUid == grid)
                    booked = uid;
            }

            Assert.That(booked, Is.Not.Null, "He books the ship a berth on the ring.");

            // With him gone a person at the desk holds the call, and can still put the booking window in front of the caller.
            hailing.End(helm);
            var roster = em.GetComponent<WGShipRosterComponent>(grid);
            var post = roster.Slots.Values.Single(s => s.Trader?.Id == "WGLupercalDockmaster");
            Assert.That(server.System<WGShipRosterSystem>().SetStoodDown((grid, roster), post.Id, true, null), Is.True);
            hailing.ClearCooldowns(helm, grid);
            Assert.That(hailing.TryHail(helm, pilot, grid), Is.True);
            server.System<SharedTransformSystem>().SetCoordinates(officer, em.GetComponent<TransformComponent>(desk).Coordinates);
            hailing.AnswerAutomated(desk, officer, true);
            // The hail window's button.
            DrydockTest.RaiseUiMessage(pair, desk, ShuttleConsoleUiKey.Key, new HailOfferServiceMessage(), officer);
            var offer = em.GetComponent<HailConsoleComponent>(helm).Options.Single();
            Assert.That(offer.Text, Is.EqualTo(Loc.GetString("drydock-hail-service")));
            Assert.That(hailing.Choose(helm, pilot, offer.Id), Is.True);
            Assert.That(ui.IsUiOpen(helm, WGDrydockConsoleUiKey.Key, pilot), "The caller gets the booking window.");
        });

        await pair.CleanReturnAsync();
    }

    private static EntityUid Single(IEntityManager em, EntityUid grid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        Assert.That(found, Has.Count.EqualTo(1), $"One {proto} aboard.");
        return found[0];
    }

    /// <summary>Powers a console directly, as the power network hasn't come up this early in a test.</summary>
    private static void SetPowered(IEntityManager em, EntityUid console)
    {
        var power = em.GetComponent<ApcPowerReceiverComponent>(console);
        power.NeedsPower = false;
        power.Powered = true;
    }
}
