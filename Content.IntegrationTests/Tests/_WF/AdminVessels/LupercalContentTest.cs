#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Tests._WF.Drydock;
using Content.IntegrationTests.Tests._WF.ShipAccess;
using Content.Server._NF.Bank;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.ShipAccess;
using Content.Server._WF.ShipCommand;
using Content.Server._WF.VehicleShop;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Explosion.EntitySystems;
using Content.Server.Cargo.Components;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.Components;
using Content.Server.Power.Generator;
using Content.Server.Station.Systems;
using Content.Server.Storage.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.AdminVessels;
using Content.Shared._FarHorizons.Power.Generation.FissionGenerator;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.Drydock;
using Content.Shared._WF.NpcCrew;
using Content.Shared._WF.ShipAccess;
using Content.Shared._WF.ShipCommand;
using Content.Shared._WF.ShipShields;
using Content.Shared._WF.VehicleShop;
using Content.Shared.Access.Systems;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Damage;
using Content.Shared.DeviceLinking;
using Content.Shared.Emag.Components;
using Content.Shared.Emag.Systems;
using Content.Shared.Explosion;
using Content.Shared.Tiles;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Materials;
using Content.Shared.NodeContainer;
using Content.Shared.Power.Generator;
using Content.Shared.Radiation.Components;
using Content.Shared.UserInterface;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.AdminVessels;

/// <summary>
/// The Lupercal's v0.14 content: its standing orders and Docks zone, the staff-set shield, the plumbed cryo pods, and
/// the vehicle terminal charging a commanded ship's prices.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipShieldControlSystem))]
public sealed class LupercalContentTest
{
    /// <summary>
    /// Every crane can work (clear arm lane, power, and an open pad that keeps the ship's air in), the reactor reads full,
    /// and every APC meets its own rooms' demand.
    /// </summary>
    [Test]
    public async Task BerthsAndFuelAreReady()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var ticks = (int) server.ResolveDependency<IGameTiming>().TickRate;
        var map = await pair.CreateTestMap();
        EntityUid grid = default;

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
        });

        // Long enough for the two-second lane scan, the power network, and the air to find any hole in the hull.
        await pair.RunTicksSync(ticks * 30);

        await server.WaitAssertion(() =>
        {
            var atmos = server.System<AtmosphereSystem>();
            var found = 0;
            var faults = new List<string>();
            var cranes = em.EntityQueryEnumerator<WFDrydockCraneComponent, PowerConsumerComponent, TransformComponent>();
            while (cranes.MoveNext(out var uid, out var crane, out var power, out var xform))
            {
                if (xform.GridUid != grid)
                    continue;

                found++;
                if (crane.Blocker != null)
                    faults.Add($"crane at {xform.LocalPosition}: lane blocked by {crane.Blocker}");
                if (power.ReceivedPower < power.DrawRate * 0.5f)
                    faults.Add($"crane at {xform.LocalPosition}: {power.ReceivedPower} of {power.DrawRate} W");

                // Three tiles behind the crane is the room its pad is sealed from.
                var inboard = xform.LocalPosition + xform.LocalRotation.RotateVec(new Vector2(0f, 3f));
                var tile = new Vector2i((int) MathF.Floor(inboard.X), (int) MathF.Floor(inboard.Y));
                var pressure = atmos.GetTileMixture(grid, map.MapUid, tile)?.Pressure ?? 0f;
                if (pressure < 90f)
                    faults.Add($"room behind the crane at {xform.LocalPosition}: {pressure} kPa at {tile}");
            }

            Assert.That(found, Is.EqualTo(8), "Two cranes on each of the four berths.");
            Assert.That(faults, Is.Empty, string.Join("\n", faults));

            // The helm's fuel gauge reads the fusion reactor, the one installed generator that burns anything.
            var reactor = OfProto(em, grid, "WFLupercalReactor").Single();
            var tank = em.GetComponent<MaterialStorageComponent>(reactor);
            Assert.That(tank.StorageLimit, Is.EqualTo(10000), "The gauge needs the reactor's capacity.");
            // Not exactly full: the ship's engineers start it as soon as they reach it.
            Assert.That(server.System<SharedMaterialStorageSystem>().GetMaterialAmount(reactor, "FuelGradePlasma", tank), Is.GreaterThan(9000), "The reactor starts fuelled.");

            var generator = em.GetComponent<FuelGeneratorComponent>(reactor);
            server.System<GeneratorSystem>().SetFuelGeneratorOn(reactor, true, generator);
            Assert.That(generator.On, Is.True, "The reactor runs.");
            Assert.That(em.GetComponent<RadiationSourceComponent>(reactor).Intensity, Is.Zero, "It gives off no radiation while it runs.");

            // The fission reactor (full size since v0.22): the hot leg joins its outlet to the turbine's inlet, the cold
            // leg the turbine's outlet to its inlet, the turbine supplies the ship's high-voltage network, and both
            // monitors are linked.
            var fission = OfProto(em, grid, "NuclearReactorNormal").Single();
            var turbine = OfProto(em, grid, "Turbine").Single();
            var core = em.GetComponent<NuclearReactorComponent>(fission);
            var spin = em.GetComponent<TurbineComponent>(turbine);
            Assert.That(core.InletEnt != null && core.OutletEnt != null && spin.InletEnt != null && spin.OutletEnt != null,
                "The reactor and the turbine have their pipe ports.");
            Assert.That(PipeGroup(em, core.OutletEnt!.Value), Is.SameAs(PipeGroup(em, spin.InletEnt!.Value)), "The hot leg is joined.");
            Assert.That(PipeGroup(em, spin.OutletEnt!.Value), Is.SameAs(PipeGroup(em, core.InletEnt!.Value)), "The cold leg is joined.");
            var shipNet = em.GetComponent<PowerSupplierComponent>(OfProto(em, grid, "WFLupercalGenerator").Single()).Net;
            Assert.That(shipNet, Is.Not.Null);
            Assert.That(em.GetComponent<PowerSupplierComponent>(turbine).Net, Is.SameAs(shipNet), "The turbine feeds the ship's high-voltage network.");
            Assert.That(em.GetComponent<DeviceLinkSourceComponent>(fission).LinkedPorts.Keys,
                Does.Contain(OfProto(em, grid, "NuclearReactorMonitor").Single()), "The reactor monitor is linked.");
            Assert.That(em.GetComponent<DeviceLinkSourceComponent>(turbine).LinkedPorts.Keys,
                Does.Contain(OfProto(em, grid, "GasTurbineMonitor").Single()), "The turbine monitor is linked.");

            // The mech bays' sortie locks (v0.22): each door bolts the other while it stands open.
            // (v0.29: the docks' inner line is external airlocks too, unlinked.)
            var outers = OfProto(em, grid, "AirlockExternalGlass")
                .Where(door => em.TryGetComponent<DeviceLinkSourceComponent>(door, out var source) && source.LinkedPorts.Count > 0)
                .ToList();
            Assert.That(outers, Has.Count.EqualTo(4), "A sortie lock in each mech bay.");
            foreach (var outer in outers)
            {
                var inner = em.GetComponent<DeviceLinkSourceComponent>(outer).LinkedPorts.Keys.Single();
                Assert.That(em.GetComponent<MetaDataComponent>(inner).EntityPrototype?.ID, Is.EqualTo("AirlockSecurityGlass"));
                Assert.That(em.GetComponent<DeviceLinkSourceComponent>(inner).LinkedPorts.Keys, Does.Contain(outer), "The interlock runs both ways.");
            }

            // The core's four gates (v0.27): command doors in pairs, interlocked (v0.29: no longer by bolting links,
            // which could hold both open).
            var gates = OfProto(em, grid, "AirlockCommandGlass")
                .Where(door => em.HasComponent<WFDoorInterlockComponent>(door))
                .ToList();
            Assert.That(gates, Has.Count.EqualTo(8), "Two interlocked doors at each of the core's four gates.");
            foreach (var door in gates)
            {
                var other = em.GetComponent<WFDoorInterlockComponent>(door).Partner!.Value;
                Assert.That(gates, Does.Contain(other), "A gate door is interlocked with the gate's other door.");
                Assert.That(em.GetComponent<WFDoorInterlockComponent>(other).Partner, Is.EqualTo(door), "The interlock runs both ways.");
            }

            // (v0.28) Built to hold: indestructible walls and windows only, and floors no crowbar, RCD or blast takes.
            using (Assert.EnterMultipleScope())
            {
                foreach (var weak in new[] { "WallReinforced", "WallSolid", "WallPlastitanium", "WFWallDurasteel", "ReinforcedWindow",
                             "PlastitaniumWindow", "ReinforcedPlasmaWindow", "PlasturaniumWindowRadProof", "Grille" })
                    Assert.That(OfProto(em, grid, weak), Is.Empty, $"No {weak} aboard.");
                Assert.That(OfProto(em, grid, "WallPlastitaniumIndestructible"), Is.Not.Empty);
                Assert.That(OfProto(em, grid, "WFLupercalWallDurasteel"), Is.Not.Empty, "The bastions keep their durasteel.");
                var prot = em.GetComponent<ProtectedGridComponent>(grid);
                Assert.That(prot.PreventFloorRemoval && prot.PreventFloorPlacement && prot.PreventRCDUse, Is.True);
            }

            var gridComp = em.GetComponent<MapGridComponent>(grid);
            var floor = server.System<SharedMapSystem>().GetTileRef(grid, gridComp, new Vector2i(0, 18));
            var broken = new List<(Vector2i GridIndices, Tile Tile)>();
            server.System<ExplosionSystem>().DamageFloorTile(floor, 1000f, 10, true, broken, protos.Index<ExplosionPrototype>("Default"));
            Assert.That(floor.Tile.IsEmpty, Is.False, "Precondition: the ring walk is floored.");
            Assert.That(broken, Is.Empty, "A blast breaks no floor tile aboard.");
        });

        // Once the substations have ramped up, every APC takes all its own rooms' draw from the network. An APC whose
        // light-voltage network joins another's supplies only a share of the joint demand, so this also finds cable
        // that touches across a room's edge (most of the ship was one network until v0.18).
        var seconds = 0;
        await DrydockTest.RunUntil(pair, 180, ticks, () =>
        {
            seconds++;
            return ShortApcs(em, grid).Count == 0;
        });
        await server.WaitAssertion(() =>
        {
            var apcs = em.EntityQueryEnumerator<ApcComponent, TransformComponent>();
            var count = 0;
            while (apcs.MoveNext(out _, out _, out var xform))
                count += xform.GridUid == grid ? 1 : 0;

            Assert.That(count, Is.GreaterThan(40), "The ship has its APCs.");
            var shortfall = ShortApcs(em, grid);
            var heavy = new List<string>();
            var loads = em.EntityQueryEnumerator<ApcPowerReceiverComponent, MetaDataComponent, TransformComponent>();
            while (loads.MoveNext(out _, out var load, out var meta, out var xform))
            {
                if (xform.GridUid == grid && load.Load >= 5000)
                    heavy.Add($"{meta.EntityPrototype?.ID} at {xform.LocalPosition}: {load.Load:0} W");
            }

            Assert.That(shortfall, Is.Empty, $"After {seconds} s:\n" + string.Join("\n", shortfall.Take(12))
                                              + "\nHeavy loads:\n" + string.Join("\n", heavy));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The ship's APCs that don't meet their own network's demand from what they receive, as readable lines.</summary>
    private static List<string> ShortApcs(IEntityManager em, EntityUid grid)
    {
        var found = new List<string>();
        var apcs = em.EntityQueryEnumerator<ApcComponent, PowerNetworkBatteryComponent, TransformComponent>();
        while (apcs.MoveNext(out _, out _, out var battery, out var xform))
        {
            var nb = battery.NetworkBattery;
            if (xform.GridUid == grid && (nb.CurrentReceiving < nb.LoadingNetworkDemand * 0.99f - 1f
                                          || nb.CurrentSupply < nb.LoadingNetworkDemand * 0.99f - 1f))
                found.Add($"APC at {xform.LocalPosition}: demand {nb.LoadingNetworkDemand:0}, receiving {nb.CurrentReceiving:0}, supplying {nb.CurrentSupply:0}");
        }

        return found;
    }

    [Test]
    public async Task OrdersZonesShieldAndCryo()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var protos = server.ResolveDependency<IPrototypeManager>();
        var map = await pair.CreateTestMap();
        EntityUid grid = default;
        (EntityUid Canister, EntityUid Shield) unbroken = default;

        await server.WaitPost(() =>
        {
            var vessel = protos.Index<VesselPrototype>("WFLupercal");
            Assert.That(server.System<AdminVesselSpawnSystem>().TrySpawnVessel(vessel, map.MapId, new Vector2(120, 0), null, out var spawned, createStation: true), Is.True);
            grid = spawned!.Value;
        });
        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var roster = em.GetComponent<WFShipRosterComponent>(grid);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(roster.MarineOrders, Is.EqualTo(WFShipMarineOrders.Rounds), "Marines walk the posts.");
                Assert.That(roster.GunnerPolicy, Is.EqualTo(WFGunnerPolicy.DefendDocked), "Gunners defend the docks.");
                Assert.That(roster.PilotStance, Is.EqualTo(WFShipPilotStance.FaceThreats), "The pilot holds and faces the attacker.");

                var zones = em.GetComponent<WFShipZonesComponent>(grid);
                Assert.That(zones.Zones.Single(z => z.Id == "docks").Mode, Is.EqualTo(WFShipZoneMode.Locked), "Docks spawn locked down (v0.29).");
                Assert.That(server.System<WFShipCommandSystem>().ZoneDoors(grid, "docks"), Has.Count.EqualTo(20),
                    "Three external airlocks across each arm and its two docking airlocks.");

                var station = server.System<StationSystem>().GetOwningStation(grid);
                Assert.That(station, Is.Not.Null, "The ship is its own station.");
                Assert.That(em.HasComponent<StationCargoOrderDatabaseComponent>(station), Is.True, "The order console has a database to read.");
                Assert.That(em.GetComponent<StationCargoBountyDatabaseComponent>(station!.Value).Bounties, Is.Not.Empty, "The bounty console has bounties to show.");

                // Orders arrive only at a telepad an order console links to (v0.25: the ship had none, so paid orders
                // never came): every order console aboard feeds the bay's telepad.
                var telepad = OfProto(em, grid, "CargoTelepad").Single();
                var sources = em.GetComponent<DeviceLinkSinkComponent>(telepad).LinkedSources;
                var consoles = OfProto(em, grid, "ComputerCargoOrders");
                Assert.That(consoles, Is.Not.Empty, "The ship has order consoles.");
                foreach (var console in consoles)
                    Assert.That(sources, Does.Contain(console), "Each order console delivers to the bay's telepad.");
            }

            // Cryo: each pod shares a pipe network with the freezer through the port under it.
            var nodes = server.System<NodeContainerSystem>();
            var freezer = OfProto(em, grid, "GasThermoMachineFreezerEnabled").Single();
            Assert.That(nodes.TryGetNode(freezer, "pipe", out PipeNode? cold), Is.True);
            var pods = OfProto(em, grid, "CryoPod");
            Assert.That(pods, Has.Count.EqualTo(3));
            foreach (var pod in pods)
            {
                Assert.That(nodes.TryGetNode(pod, "port", out PipeNode? port), Is.True);
                Assert.That(port!.NodeGroup, Is.SameAs(cold!.NodeGroup), "The pod draws from the cold line.");
            }

            Assert.That(em.GetComponent<GasThermoMachineComponent>(freezer).TargetTemperature, Is.LessThan(100f), "The freezer is set cold enough for cryo.");

            foreach (var miner in new[] { "GasMinerPlasma", "GasMinerTritium", "GasMinerFrezon", "GasMinerNitrousOxide", "GasMinerWaterVapor", "GasMinerAmmonia", "GasMinerCarbonDioxide" })
                Assert.That(OfProto(em, grid, miner), Has.Count.EqualTo(1), $"{miner} aboard.");
        });

        await server.WaitAssertion(() =>
        {
            var shield = OfProto(em, grid, "WFLupercalShieldGenerator").Single();
            var control = server.System<WFShipShieldControlSystem>();
            var hands = server.System<SharedHandsSystem>();
            var (visitor, _) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Random Stranger", 3);
            var (staffer, card) = ShipAccessTest.SpawnPersonWithCard(em, hands, map, "Ben Ortiz", 2);
            server.System<WFShipAccessServerSystem>().SetupRegisteredShip(grid, "Ada Vance", new List<NetUserId>());
            Assert.That(server.System<WFShipAccessSystem>().TryGetKey(card, out var key), Is.True);
            Assert.That(server.System<WFShipCommandSystem>().AddPerson((grid, em.GetComponent<WFShipCommandComponent>(grid)),
                WFShipCommandList.Staff, key, null, "Ben Ortiz"), Is.True);

            var refused = new ActivatableUIOpenAttemptEvent(visitor);
            em.EventBus.RaiseLocalEvent(shield, refused);
            var allowed = new ActivatableUIOpenAttemptEvent(staffer);
            em.EventBus.RaiseLocalEvent(shield, allowed);
            Assert.That(refused.Cancelled, Is.True, "A visitor can't open the generator.");
            Assert.That(allowed.Cancelled, Is.False, "Someone on the staff list can.");

            var emitter = em.GetComponent<ShipShieldEmitterComponent>(shield);
            emitter.Damage = emitter.DamageLimit / 2f;
            var heal = emitter.HealPerSecond;

            // A visitor's request changes nothing, whichever way it arrives.
            DrydockTest.RaiseUiMessage(pair, shield, WFShipShieldUiKey.Key, new WFShipShieldSetStrengthMessage(9f), visitor);
            Assert.That(emitter.DamageLimit, Is.EqualTo(500000f), "A visitor doesn't set the strength.");

            DrydockTest.RaiseUiMessage(pair, shield, WFShipShieldUiKey.Key, new WFShipShieldSetStrengthMessage(2000000f), staffer);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(emitter.DamageLimit, Is.EqualTo(2000000f), "Staff set the strength.");
                Assert.That(emitter.Damage, Is.EqualTo(1000000f).Within(1f), "The field keeps its share of health.");
                Assert.That(emitter.HealPerSecond, Is.EqualTo(heal * 4f).Within(0.01f), "It refills in the same time.");
                Assert.That(WFShipShieldEffects.EffectiveCapacity(emitter.DamageLimit, emitter.MaxDraw, emitter.PowerModifier, emitter.DamageExp),
                    Is.EqualTo(2000000f).Within(1f), "Power demand doesn't cap the new strength.");
                Assert.That(control.SetStrength((shield, emitter), float.NaN), Is.False);
                Assert.That(control.SetStrength((shield, emitter), 0f), Is.False);
            }

            // Staff-side lockers follow the Staff lockers switch.
            var reader = server.System<AccessReaderSystem>();
            var lockers = new List<EntityUid>();
            var rules = em.EntityQueryEnumerator<WFDoorAccessRuleComponent, EntityStorageComponent>();
            while (rules.MoveNext(out var uid, out var rule, out _))
            {
                if (rule.Zone == "lockers")
                    lockers.Add(uid);
            }

            Assert.That(lockers, Has.Count.GreaterThan(0), "The staff side has lockers on the switch.");
            using (Assert.EnterMultipleScope())
            {
                foreach (var locker in lockers)
                {
                    Assert.That(reader.IsAllowed(visitor, locker), Is.False, "A visitor can't open a staff locker.");
                    Assert.That(reader.IsAllowed(staffer, locker), Is.True, "Someone on the staff list can.");
                }
            }

            // (v0.27) Only the staff list and up open maintenance panels aboard: nobody else hacks a door or a machine.
            var wires = server.System<SharedWiresSystem>();
            var door = OfProto(em, grid, "AirlockCommandGlass").First();
            var panel = em.GetComponent<WiresPanelComponent>(door);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(em.GetComponent<WFShipCommandComponent>(grid).StaffOnlyWires, Is.True, "The Lupercal's wires are staff-only.");
                Assert.That(wires.CanTogglePanel((door, panel), visitor), Is.False, "A visitor can't open a door's panel.");
                Assert.That(wires.CanTogglePanel((door, panel), staffer), Is.True, "Someone on the staff list can.");
                Assert.That(wires.CanTogglePanel((door, panel), null), Is.True, "Nor is a panel worked by no one held shut.");
            }

            // (v0.28) Nor does an emag work for anyone below the staff list.
            var emag = em.SpawnEntity("Emag", em.GetComponent<TransformComponent>(door).Coordinates);
            var visitorEmag = new WFEmagAttemptEvent(visitor, door);
            em.EventBus.RaiseEvent(EventSource.Local, ref visitorEmag);
            var stafferEmag = new WFEmagAttemptEvent(staffer, door);
            em.EventBus.RaiseEvent(EventSource.Local, ref stafferEmag);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(server.System<EmagSystem>().TryEmagEffect(emag, visitor, door), Is.False, "A visitor's emag does nothing aboard.");
                Assert.That(em.HasComponent<EmaggedComponent>(door), Is.False, "The door isn't emagged.");
                Assert.That(visitorEmag.Cancelled, Is.True, "The ship refuses a visitor's emag.");
                Assert.That(stafferEmag.Cancelled, Is.False, "It lets its staff's through.");
            }

            // Admin gas stock, and what can't be broken.
            var canister = OfProto(em, grid, "WFLupercalCanisterPlasma").Single();
            var gas = em.GetComponent<GasCanisterComponent>(canister);
            Assert.That(gas.Air.Volume, Is.EqualTo(1500000f), "A thousand times the usual canister.");
            Assert.That(gas.Air.TotalMoles, Is.GreaterThan(2000000f), "And nearly all of its gas still in it.");

            var blunt = new DamageSpecifier(protos.Index<DamageTypePrototype>("Blunt"), FixedPoint2.New(1000000));
            var damage = server.System<DamageableSystem>();
            damage.TryChangeDamage(canister, blunt, ignoreResistances: true);
            damage.TryChangeDamage(shield, blunt, ignoreResistances: true);
            unbroken = (canister, shield);
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(unbroken.Canister), Is.False, "An admin canister can't be destroyed.");
            Assert.That(em.Deleted(unbroken.Shield), Is.False, "Nor can the shield generator.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task VehicleTerminalChargesTheShipsPrices()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        var shop = server.System<WFVehicleShopSystem>();
        var services = server.System<WFShipServiceSystem>();
        var catalog = server.ProtoMan.Index<WFVehicleCatalogPrototype>("WFVehicleCatalogExpeditionary");
        var index = catalog.Vehicles.FindIndex(listing => listing.Vehicle == "VehicleBoxVan");
        var price = catalog.Vehicles[index].Price;
        EntityUid terminal = default, payer = default;

        await server.WaitPost(() =>
        {
            var maps = server.System<SharedMapSystem>();
            var plating = server.Resolve<ITileDefinitionManager>()["Plating"].TileId;
            for (var x = -5; x < 25; x++)
            for (var y = -5; y < 25; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), new Tile(plating));

            // The payer is the test's admin session, so with admins served free the rule would never apply.
            var command = em.EnsureComponent<WFShipCommandComponent>(map.Grid.Owner);
            command.OwnerServedFree = command.AdminsServedFree = false;
            services.SetRule((map.Grid.Owner, command), new WFShipPriceRule { Name = "half", Services = WFShipServiceKind.Vehicles, Multiplier = 0.5f }, null);
            terminal = em.SpawnEntity("WFComputerVehicleShopExpeditionary", new EntityCoordinates(map.Grid.Owner, 3.5f, 3.5f));
            em.SpawnEntity("WFVehicleDeliveryPad", new EntityCoordinates(map.Grid.Owner, 12.5f, 12.5f));
            payer = DrydockTest.CreatePayer(pair, map.MapId);
            server.System<BankSystem>().TryBankDeposit(payer, price * 2, tax: false);
        });
        await server.WaitRunTicks(5);

        var before = 0;
        await server.WaitPost(() =>
        {
            before = em.GetComponent<BankAccountComponent>(payer).Balance;
            em.EventBus.RaiseLocalEvent(terminal, new WFVehicleShopBuyMessage(index) { Actor = payer });
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var ent = (terminal, em.GetComponent<WFVehicleShopComponent>(terminal));
            var (_, status, vehicle) = shop.InspectPad(ent);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(status, Is.EqualTo(WFVehicleShopPad.Vehicle), "The van was delivered.");
                Assert.That(em.GetComponent<BankAccountComponent>(payer).Balance, Is.EqualTo(before - price / 2), "The rule set the charge.");
                Assert.That(shop.SellPrice(ent, vehicle!.Value), Is.LessThanOrEqualTo(price / 2), "A bargain can't be sold back at a profit.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The pipe network a reactor or turbine port's primary-layer node is on.</summary>
    private static object? PipeGroup(IEntityManager em, EntityUid port)
    {
        return em.GetComponent<NodeContainerComponent>(port).Nodes["pipe"].NodeGroup;
    }

    private static List<EntityUid> OfProto(IEntityManager em, EntityUid grid, string proto)
    {
        var found = new List<EntityUid>();
        var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var meta, out var xform))
        {
            if (xform.GridUid == grid && meta.EntityPrototype?.ID == proto)
                found.Add(uid);
        }

        return found;
    }
}
