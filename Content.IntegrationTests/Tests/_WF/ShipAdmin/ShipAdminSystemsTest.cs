#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._WF.ShipAdmin;
using Content.Server._Mono.Cleanup;
using Content.Server._WF.Administration.Systems;
using Content.Server._WF.Drydock;
using Content.Server._WF.ShipAdmin;
using Content.Server.Administration.Managers;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._WF.ShipAdmin;
using Content.Shared.Shuttles.Components;
using Content.Shared.Tiles;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.ShipAdmin;

/// <summary>
/// The panel renames a ship, changes its radar signature, switches protections on and off again, sets and restores
/// its shield strength, and brings a refit line up a tier that the ship then owns.
/// </summary>
[TestFixture]
[TestOf(typeof(WFShipAdminSystem))]
public sealed class ShipAdminSystemsTest
{
    private const string VesselProto = "Guppy";
    private const string ShieldProto = "ShieldGeneratorPOI";

    [Test]
    public async Task ShipIsRenamedSignedProtectedAndRefitted()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ships = entMan.System<WFShipAdminSystem>();
        var spawner = entMan.System<AdminVesselSpawnSystem>();
        var refits = entMan.System<WFShipRefitSystem>();
        var admins = server.ResolveDependency<IAdminManager>();
        var map = await pair.CreateTestMap();
        var session = server.PlayerMan.GetSessionById(pair.Client.Session!.UserId);

        EntityUid grid = default;
        string refitId = string.Empty;
        var wasAdmin = false;
        var refitting = false;
        WFShipAdminEuiState state = default!;

        await server.WaitAssertion(() =>
        {
            wasAdmin = admins.IsAdmin(session);
            if (!wasAdmin)
                admins.PromoteHost(session);

            var vessel = server.ProtoMan.Index<VesselPrototype>(VesselProto);
            Assert.That(spawner.TrySpawnVessel(vessel, map.MapId, new Vector2(80, 0), null, out var spawned), Is.True);
            grid = spawned!.Value;
            var netGrid = entMan.GetNetEntity(grid);

            Assert.That(ships.Rename(session, grid, "  ", out var result), Is.False, "A blank name is refused.");
            Assert.That(ships.Rename(session, grid, "Staff Test", out result), Is.True, result);
            Assert.That(entMan.GetComponent<MetaDataComponent>(grid).EntityName, Is.EqualTo("Staff Test"));

            Assert.That(ships.SetIff(session, grid, new WFShipAdminIffMessage(netGrid, "not a colour", null, null), out result), Is.False);
            Assert.That(ships.SetIff(session, grid, new WFShipAdminIffMessage(netGrid, "#102030", true, null), out result), Is.True, result);
            var iff = entMan.GetComponent<IFFComponent>(grid);
            Assert.Multiple(() =>
            {
                Assert.That(iff.Color, Is.EqualTo(Color.FromHex("#102030")));
                Assert.That((iff.Flags & IFFFlags.Hide) != 0, Is.True);
            });
            Assert.That(ships.SetIff(session, grid, new WFShipAdminIffMessage(netGrid, null, false, null), out result), Is.True, result);
            Assert.That((iff.Flags & IFFFlags.Hide) != 0, Is.False);

            foreach (var protect in new[] { WFShipAdminProtect.Blastproof, WFShipAdminProtect.Pacified, WFShipAdminProtect.CleanupImmune, WFShipAdminProtect.GunsDisabled, WFShipAdminProtect.FtlUnrestricted })
            {
                Assert.That(ships.SetProtect(session, grid, protect, true, out result), Is.True, result);
                Assert.That(ships.Protections(grid).HasFlag(protect), Is.True, protect.ToString());
            }

            Assert.That(entMan.HasComponent<CleanupImmuneComponent>(grid), Is.True);
            Assert.That(entMan.GetComponent<ProtectedGridComponent>(grid).PreventExplosions, Is.True);
            Assert.That(ships.SetProtect(session, grid, WFShipAdminProtect.Blastproof, false, out result), Is.True, result);
            Assert.That(entMan.HasComponent<ProtectedGridComponent>(grid), Is.False, "A protection the panel added is taken off whole.");
            Assert.That(ships.SetProtect(session, grid, WFShipAdminProtect.CleanupImmune, false, out result), Is.True, result);
            Assert.That(ships.Protections(grid).HasFlag(WFShipAdminProtect.CleanupImmune), Is.False);

            // Shield strength: refused without a generator, then set and restored on one.
            Assert.That(ships.SetShieldStrength(session, map.Grid.Owner, 50f, out result), Is.False, "A grid with no shield generator has none to set.");
            var gridComp = entMan.GetComponent<MapGridComponent>(grid);
            var floor = entMan.System<SharedMapSystem>().GetAllTiles(grid, gridComp).First().GridIndices;
            entMan.SpawnEntity(ShieldProto, new EntityCoordinates(grid, new Vector2(floor.X + 0.5f, floor.Y + 0.5f)));
            Assert.That(ships.SetShieldStrength(session, grid, 150f, out result), Is.False, "Strength is a percentage.");
            Assert.That(ships.SetShieldStrength(session, grid, 40f, out result), Is.True, result);
            Assert.That(ships.BuildDetail(grid)!.ShieldPercent, Is.EqualTo(40));
            var restored = Loc.GetString("wf-ship-admin-result-shields-restored", ("ship", "Staff Test"));
            Assert.That(ships.SetShieldStrength(session, grid, 99.6f, out result), Is.True, result);
            Assert.That(result, Is.Not.EqualTo(restored), "Just under a hundred is not a restore, though it rounds to one.");
            Assert.That(ships.SetShieldStrength(session, grid, 100f, out result), Is.True, result);
            Assert.That(result, Is.EqualTo(restored));
            Assert.That(ships.BuildDetail(grid)!.ShieldPercent, Is.EqualTo(100));

            Assert.That(ships.FitBestParts(session, grid, out result), Is.True, result);
            Assert.That(ships.ApplyModifier(session, grid, "NoSuchModifier", out result), Is.False);
            Assert.That(ships.StartRefit(session, grid, "NoSuchRefit", 1, out result), Is.False);

            // The first refit line the design has parts for.
            var lines = ships.Refits(grid);
            Assert.That(lines, Is.Not.Empty, "Precondition: refit lines exist.");
            foreach (var line in lines)
            {
                if (!ships.StartRefit(session, grid, line.Id, 1, out result))
                    continue;

                refitId = line.Id;
                refitting = true;
                break;
            }

            Assert.That(refitting, Is.True, $"No refit line starts on a {VesselProto}: {result}");

            state = new WFShipAdminEuiState
            {
                Ships = ships.BuildList(WFShipAdminFilter.PlayerShips, out _),
                Selected = ships.BuildDetail(grid),
                ViewerFlags = ships.Flags(session),
            };
        });

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(ships.TryGetFix(grid, out _, out _), Is.False, "The refit has finished.");
            Assert.That(refits.GetRefit(grid, refitId).Tier, Is.EqualTo(1), "The ship owns the tier it was brought up to.");
            Assert.That(ships.Refits(grid).Single(line => line.Id == refitId).Tier, Is.EqualTo(1));
        });

        await pair.Client.WaitAssertion(() =>
        {
            var window = new WFShipAdminWindow();
            window.OpenCentered();
            window.UpdateState(state);
            Assert.That(window.Upgrades.RefitCount, Is.EqualTo(state.Selected!.Refits.Count));
            Assert.That(state.Selected.ShieldPercent, Is.Not.Null, "The detail carries the shield strength.");
            Assert.That(window.Systems.ShieldStatus, Does.Contain(state.Selected.ShieldPercent.ToString()));
            window.Close();
        });

        await server.WaitPost(() =>
        {
            entMan.DeleteEntity(grid);
            if (!wasAdmin)
                admins.DeAdmin(session);
        });

        await pair.CleanReturnAsync();
    }
}
