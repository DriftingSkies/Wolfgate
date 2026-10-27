#nullable enable
using System.Collections.Generic;
using Content.Server._WF.MappingMode;
using Content.Server._WF.MappingMode.Test;
using Content.Server.Ghost;
using Content.Shared._WF.MappingMode;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using static Content.IntegrationTests.Tests._WF.MappingMode.WFMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WF.MappingMode;

/// <summary>
/// A mapper can't ghost from their observer or their test character, so they never end up in nullspace; a Mapping
/// admin still can.
/// </summary>
[TestFixture]
public sealed class WFMappingGhostTest
{
    [Test]
    public async Task MappersCantGhost()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WFMappingModeSystem>();
        var testing = entMan.System<WFMappingTestSystem>();
        var ghosts = entMan.System<GhostSystem>();
        var minds = entMan.System<SharedMindSystem>();
        var mapSys = entMan.System<MapSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair, demoteClient: false);
        var a = (await AddMappers(pair, 1))[0];
        var admin = pair.Player!;

        bool Ghost(Robust.Shared.Player.ICommonSession player)
        {
            Assert.That(minds.TryGetMind(player, out var mindId, out var mind), Is.True);
            return ghosts.OnGhostAttempt(mindId, true, viaCommand: true, mind: mind);
        }

        await server.WaitAssertion(() =>
        {
            server.CfgMan.SetCVar(WFMappingModeCVars.TestCooldown, 0f);
            Assert.That(mapping.TryCreateSession(a, "Ghost Test", out var session), Is.True);
            var observer = a.AttachedEntity!.Value;
            var grid = mapSys.CreateGridEntity(entMan.GetComponent<MapComponent>(session!.RootMap).MapId);
            var plating = new Tile(tileDefs["Plating"].TileId);
            mapSys.SetTiles(grid, grid.Comp, new List<(Vector2i, Tile)> { (Vector2i.Zero, plating), (new Vector2i(0, 1), plating) });

            Assert.That(Ghost(a), Is.False, "A mapper can't ghost from their observer.");
            Assert.That(a.AttachedEntity, Is.EqualTo(observer));

            Assert.That(testing.TryStart(a), Is.True);
            var mob = a.AttachedEntity!.Value;
            Assert.That(mob, Is.Not.EqualTo(observer));
            Assert.That(Ghost(a), Is.False, "A mapper can't ghost from their test character.");
            Assert.Multiple(() =>
            {
                Assert.That(a.AttachedEntity, Is.EqualTo(mob));
                Assert.That(entMan.HasComponent<GhostComponent>(a.AttachedEntity), Is.False);
            });

            Assert.That(mapping.TryCreateSession(admin, "Admin Ghost Test", out _), Is.True);
            Assert.That(entMan.HasComponent<WFMappingObserverComponent>(admin.AttachedEntity), Is.True);
            Assert.That(Ghost(admin), Is.True, "A Mapping admin can ghost.");
        });

        await pair.RunTicksSync(5);
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<GhostComponent>(admin.AttachedEntity), Is.True));

        await Disable(pair);
        await pair.CleanReturnAsync();
    }
}
