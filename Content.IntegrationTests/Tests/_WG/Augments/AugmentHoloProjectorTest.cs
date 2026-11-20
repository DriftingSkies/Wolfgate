using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Augments;
using Content.Server.Atmos.Components;
using Content.Shared._WG.Augments;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Doors.Components;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Preferences;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Augments;

/// <summary>
/// The holo projector: the body swaps into one copy's place and leaves the other in its own, nobody lands past a wall
/// or an open door, a closet or a doorway won't cast, and a struck copy pops.
/// </summary>
[TestFixture]
[TestOf(typeof(WGHoloProjectorSystem))]
public sealed class AugmentHoloProjectorTest
{
    [Test]
    public async Task HoloProjectorTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var maps = server.System<SharedMapSystem>();
        var transform = server.System<SharedTransformSystem>();
        var projector = server.System<WGHoloProjectorSystem>();
        var tiles = server.ResolveDependency<ITileDefinitionManager>();
        var map = await pair.CreateTestMap();
        var grid = map.Grid;

        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            // A room from x -3 to 1, walled off at x 2 from a second room, with an open firelock in the wall; and a
            // one-tile closet out at x 20.
            var plating = new Tile(tiles["Plating"].TileId);
            for (var x = -3; x <= 6; x++)
            for (var y = -3; y <= 3; y++)
                maps.SetTile(grid, grid, new Vector2i(x, y), plating);
            maps.SetTile(grid, grid, new Vector2i(20, 0), plating);

            for (var y = -3; y <= 3; y++)
                entities.SpawnEntity(y == 0 ? "Firelock" : "WallSolid", maps.GridTileToLocal(grid, grid, new Vector2i(2, y)));

            body = entities.SpawnEntity("MobHuman", maps.GridTileToLocal(grid, grid, Vector2i.Zero));
            entities.RemoveComponent<BarotraumaComponent>(body);
            server.System<SharedHumanoidAppearanceSystem>().LoadProfile(body,
                HumanoidCharacterProfile.DefaultWithSpecies("Human").WithAugmentImplants(["WGAugmentHoloProjector"]));
            Assert.That(entities.TryGetComponent<WGHoloProjectorComponent>(body, out var comp), "The projector wasn't fitted.");
            Assert.That(comp!.ActionEntity, Is.Not.Null, "The projector has no action.");
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WGHoloProjectorComponent>(body);
            var firelock = maps.GetAnchoredEntities(grid, grid, new Vector2i(2, 0)).Single(entities.HasComponent<DoorComponent>);
            Assert.That(entities.GetComponent<DoorComponent>(firelock).State, Is.EqualTo(DoorState.Open));

            Vector2i TileOf(EntityUid uid) => maps.TileIndicesFor(grid, grid, entities.GetComponent<TransformComponent>(uid).Coordinates);
            List<EntityUid> Decoys()
            {
                var found = new List<EntityUid>();
                var query = entities.EntityQueryEnumerator<WGHoloDecoyComponent>();
                while (query.MoveNext(out var uid, out _))
                    found.Add(uid);
                return found;
            }

            for (var i = 0; i < 20; i++)
            {
                transform.SetCoordinates(body, maps.GridTileToLocal(grid, grid, Vector2i.Zero));
                Assert.That(projector.TryCast((body, comp)), "The cast failed in an open room.");

                var landed = TileOf(body);
                Assert.That(landed, Is.Not.EqualTo(Vector2i.Zero), "The body didn't move.");
                Assert.That(landed.X, Is.LessThan(2), "The body landed past the wall.");

                var decoys = Decoys();
                Assert.That(decoys, Has.Count.EqualTo(comp.Copies));
                Assert.That(decoys.Select(TileOf), Has.Member(Vector2i.Zero), "No copy took the body's place.");
                Assert.That(decoys.Select(TileOf).All(t => t.X < 2), "A copy landed past the wall.");
                foreach (var decoy in decoys)
                {
                    Assert.That(entities.GetComponent<WGHoloDecoyComponent>(decoy).Source, Is.EqualTo(body));
                    Assert.That(entities.GetComponent<MetaDataComponent>(decoy).EntityName,
                        Is.EqualTo(entities.GetComponent<MetaDataComponent>(body).EntityName));
                    entities.DeleteEntity(decoy);
                }
            }

            // A closet or a doorway won't cast, and nothing moves.
            foreach (var cramped in new[] { new Vector2i(20, 0), new Vector2i(2, 0) })
            {
                transform.SetCoordinates(body, maps.GridTileToLocal(grid, grid, cramped));
                Assert.That(projector.TryCast((body, comp)), Is.False, $"The cast went off at {cramped}.");
                Assert.That(TileOf(body), Is.EqualTo(cramped));
                Assert.That(Decoys(), Is.Empty);
            }

            // A struck copy pops.
            transform.SetCoordinates(body, maps.GridTileToLocal(grid, grid, Vector2i.Zero));
            Assert.That(projector.TryCast((body, comp)));
            var struck = Decoys()[0];
            server.System<DamageableSystem>().TryChangeDamage(struck,
                new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Blunt"), 5));
            Assert.That(entities.Deleted(struck) || entities.IsQueuedForDeletion(struck), "A struck copy stayed.");
        });

        await server.WaitPost(() =>
        {
            var left = new List<EntityUid>();
            var query = entities.EntityQueryEnumerator<WGHoloDecoyComponent>();
            while (query.MoveNext(out var decoy, out _))
                left.Add(decoy);
            foreach (var decoy in left)
                entities.DeleteEntity(decoy);
            entities.DeleteEntity(body);
        });
        await pair.CleanReturnAsync();
    }
}
