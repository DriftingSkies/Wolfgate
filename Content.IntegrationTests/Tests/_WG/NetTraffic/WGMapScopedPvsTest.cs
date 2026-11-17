using System.Numerics;
using Content.Server._WG.NetTraffic;
using Content.Shared._WG.NetTraffic;
using Robust.Server.GameObjects;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NetTraffic;

/// <summary>A player gets the grids of the system they look at, and those of another only once they go there.</summary>
[TestFixture]
[TestOf(typeof(WGMapScopedPvsSystem))]
public sealed partial class WGMapScopedPvsTest
{
    [Test]
    public async Task OnlyTheViewedSystemsGridsAreSent()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var em = server.EntMan;
        var cEm = pair.Client.EntMan;

        // The pool runs with PVS and scoping off. PVS goes on first so the server tracks what the client holds, then
        // scoping once the maps have reached the client.
        await server.WaitPost(() => server.CfgMan.SetCVar(CVars.NetPVS, true));
        await pair.RunTicksSync(5);
        var a = await pair.CreateTestMap();
        var b = await pair.CreateTestMap();
        await server.WaitPost(() => server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, true));

        NetEntity mapA = default, mapB = default, gridA = default, gridB = default;
        await server.WaitPost(() =>
        {
            mapA = em.GetNetEntity(a.MapUid);
            mapB = em.GetNetEntity(b.MapUid);
            gridA = em.GetNetEntity(a.Grid.Owner);
            gridB = em.GetNetEntity(b.Grid.Owner);
            server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, a.MapId)));
        });
        await pair.RunTicksSync(30);

        try
        {
            await server.WaitAssertion(() =>
            {
                var scoped = server.System<WGMapScopedPvsSystem>();
                Assert.That(scoped.IsScoped(a.Grid.Owner) && scoped.IsScoped(b.Grid.Owner), "both grids are scoped");
            });
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(gridA, out _), "the viewed system's grid is sent");
                Assert.That(cEm.TryGetEntity(mapA, out _), "the viewed map is sent");
                Assert.That(cEm.TryGetEntity(gridB, out _), Is.False, "another system's grid is not sent");
                Assert.That(cEm.TryGetEntity(mapB, out _), "every map is still sent, for the FTL map");
            });

            // Going to the other system swaps what the client holds.
            await server.WaitPost(() =>
                server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, b.MapId))));
            await pair.RunTicksSync(30);
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(gridB, out _), "the new system's grid arrives");
                Assert.That(cEm.TryGetEntity(gridA, out _), Is.False, "the old system's grid is dropped");
            });

            // A grid made in the viewed system arrives at once; coming back brings the first system back.
            NetEntity late = default;
            await server.WaitPost(() =>
            {
                var maps = server.System<SharedMapSystem>();
                var grid = maps.CreateGridEntity(b.MapId);
                maps.SetTile(grid, grid.Comp, new Robust.Shared.Maths.Vector2i(40, 40), b.Tile.Tile);
                late = em.GetNetEntity(grid.Owner);
            });
            await pair.RunTicksSync(10);
            Assert.That(cEm.TryGetEntity(late, out _), "a new grid in the viewed system is sent");

            await server.WaitPost(() =>
                server.PlayerMan.SetAttachedEntity(pair.Player!, em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, a.MapId))));
            await pair.RunTicksSync(30);
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(gridA, out _), "the first system's grid comes back");
                Assert.That(cEm.TryGetEntity(late, out _), Is.False, "the second system's new grid is dropped");
            });

            // A paused system stays scoped rather than going to everyone.
            await server.WaitPost(() => server.System<SharedMapSystem>().SetPaused(b.MapUid, true));
            await pair.RunTicksSync(30);
            await server.WaitAssertion(() =>
                Assert.That(server.System<WGMapScopedPvsSystem>().IsScoped(b.Grid.Owner), "a paused system's grid stays scoped"));
            Assert.That(cEm.TryGetEntity(gridB, out _), Is.False, "a paused system's grid is not sent");
            await server.WaitPost(() => server.System<SharedMapSystem>().SetPaused(b.MapUid, false));

            // A camera on the other system brings its grids too, and only while it is watched.
            EntityUid camera = default;
            await server.WaitPost(() =>
            {
                camera = em.SpawnEntity(null, new MapCoordinates(Vector2.Zero, b.MapId));
                server.System<ViewSubscriberSystem>().AddViewSubscriber(camera, pair.Player!);
            });
            await pair.RunTicksSync(30);
            Assert.Multiple(() =>
            {
                Assert.That(cEm.TryGetEntity(gridB, out _), "a watched camera's system is sent");
                Assert.That(cEm.TryGetEntity(gridA, out _), "the body's system is kept alongside it");
            });

            await server.WaitPost(() => server.System<ViewSubscriberSystem>().RemoveViewSubscriber(camera, pair.Player!));
            await pair.RunTicksSync(30);
            Assert.That(cEm.TryGetEntity(gridB, out _), Is.False, "the camera's system goes when it stops being watched");

            // Off, every grid goes to everyone again.
            await server.WaitPost(() => server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, false));
            await pair.RunTicksSync(30);
            Assert.That(cEm.TryGetEntity(gridB, out _), "with scoping off another system's grid is sent");
        }
        finally
        {
            await server.WaitPost(() =>
            {
                server.CfgMan.SetCVar(NetTrafficCVars.MapScopedGrids, false);
                server.CfgMan.SetCVar(CVars.NetPVS, false);
            });
            await pair.RunTicksSync(10);
        }

        await pair.CleanReturnAsync();
    }
}
