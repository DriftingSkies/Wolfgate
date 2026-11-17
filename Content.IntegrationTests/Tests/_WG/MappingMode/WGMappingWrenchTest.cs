#nullable enable
using System.Numerics;
using Content.Server._WG.MappingMode;
using Content.Shared._WG.MappingTools;
using Robust.Server.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using static Content.IntegrationTests.Tests._WG.MappingMode.WGMappingModeTestUtil;

namespace Content.IntegrationTests.Tests._WG.MappingMode;

/// <summary>
/// The wrench anchors and unanchors for a plain mapper with the right physics, snaps to the tile, undoes, and refuses
/// a blocked or tileless spot, an unwrenchable entity and someone outside the session.
/// </summary>
[TestFixture]
public sealed class WGMappingWrenchTest
{
    private const string Machine = "VendingMachineCola";
    private const string Wall = "WallSolid";

    [Test]
    public async Task WrenchTogglesAndUndoes()
    {
        await using var pair = await PoolManager.GetServerClient(Settings);
        var server = pair.Server;
        var entMan = server.EntMan;
        var mapping = entMan.System<WGMappingModeSystem>();
        var mapSys = entMan.System<MapSystem>();
        var xformSys = entMan.System<TransformSystem>();
        var tileDefs = server.ResolveDependency<ITileDefinitionManager>();
        await Enable(pair);
        var dummies = await AddMappers(pair, 2);
        var mapper = dummies[0];
        var outsider = dummies[1];

        EntityUid machine = default;
        EntityUid other = default;
        EntityUid wall = default;
        EntityUid gridUid = default;
        await server.WaitAssertion(() =>
        {
            Assert.That(mapping.TryCreateSession(mapper, "Wrench Test", out var session), Is.True);
            var mapId = entMan.GetComponent<TransformComponent>(session!.RootMap).MapID;
            var grid = mapSys.CreateGridEntity(mapId);
            gridUid = grid.Owner;
            var plating = new Tile(tileDefs["Plating"].TileId);
            for (var x = 0; x < 4; x++)
            {
                mapSys.SetTile(grid, grid.Comp, new Vector2i(x, 0), plating);
            }

            machine = entMan.SpawnEntity(Machine, new EntityCoordinates(gridUid, 0.5f, 0.5f));
            other = entMan.SpawnEntity(Machine, new EntityCoordinates(gridUid, 1.5f, 0.5f));
            wall = entMan.SpawnEntity(Wall, new EntityCoordinates(gridUid, 2.5f, 0.5f));
            Assert.That(entMan.GetComponent<TransformComponent>(machine).Anchored, Is.True);
        });

        void Wrench(EntityUid target, Robust.Shared.Player.ICommonSession who)
        {
            RaiseAs(entMan, who, new MappingToolsWrenchEvent(entMan.GetNetEntity(target)));
        }

        bool Anchored(EntityUid uid) => entMan.GetComponent<TransformComponent>(uid).Anchored;
        BodyType Body(EntityUid uid) => entMan.GetComponent<PhysicsComponent>(uid).BodyType;

        await server.WaitAssertion(() =>
        {
            // Someone outside the session can't touch it.
            Wrench(machine, outsider);
            Assert.That(Anchored(machine), Is.True, "An outsider's wrench does nothing.");

            // A wall takes no wrench.
            Wrench(wall, mapper);
            Assert.That(Anchored(wall), Is.True, "A wall isn't wrenchable.");

            Wrench(machine, mapper);
            Assert.That(Anchored(machine), Is.False, "The wrench unanchors.");
            Assert.That(Body(machine), Is.EqualTo(BodyType.Dynamic), "A loose machine has a loose body.");

            RaiseAs(entMan, mapper, new MappingToolsHistoryEvent(false));
            Assert.That(Anchored(machine), Is.True, "Undo anchors it again.");
            Assert.That(Body(machine), Is.EqualTo(BodyType.Static));

            RaiseAs(entMan, mapper, new MappingToolsHistoryEvent(true));
            Assert.That(Anchored(machine), Is.False, "Redo unanchors it.");

            // Anchoring snaps it to the tile and a right angle.
            xformSys.SetLocalPositionRotation(machine, new Vector2(0.3f, 0.8f), Angle.FromDegrees(80));
            Wrench(machine, mapper);
            Assert.That(Anchored(machine), Is.True, "The wrench anchors.");
            Assert.That(Body(machine), Is.EqualTo(BodyType.Static));
            var xform = entMan.GetComponent<TransformComponent>(machine);
            Assert.That(xform.LocalPosition, Is.EqualTo(new Vector2(0.5f, 0.5f)));
            Assert.That(xform.LocalRotation.Degrees % 90, Is.EqualTo(0).Within(0.01));

            // Anchoring is refused on a tile a machine already blocks.
            Wrench(other, mapper);
            Assert.That(Anchored(other), Is.False);
            xformSys.SetLocalPosition(other, new Vector2(0.5f, 0.5f));
            Wrench(other, mapper);
            Assert.That(Anchored(other), Is.False, "That tile is blocked.");

            // And off the floor.
            xformSys.SetLocalPosition(other, new Vector2(10.5f, 10.5f));
            Wrench(other, mapper);
            Assert.That(Anchored(other), Is.False, "There's no tile there.");
        });

        await pair.CleanReturnAsync();
    }
}
