#nullable enable annotations

using System.Collections.Generic;
using System.Numerics;
using Content.Shared._WG.Pool;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WG.Pool;

/// <summary>A pool table in the world: its pieces share one game, shooting takes a side and a cue, and walking off gives the side up.</summary>
public sealed class PoolTableTest
{
    [Test]
    public async Task PiecesShareOneGameAndShotsNeedACue()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var ui = em.System<SharedUserInterfaceSystem>();
        var xforms = em.System<SharedTransformSystem>();

        EntityUid root = default;
        EntityUid farPiece = default;
        EntityUid alice = default;
        EntityUid bob = default;

        await server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            var pieces = new List<EntityUid>();
            for (var x = 0; x < 3; x++)
            {
                for (var y = 0; y < 2; y++)
                {
                    maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Vector2i(x, y), map.Tile.Tile);
                    var piece = em.SpawnEntity("CoyoteTableSnooker", new EntityCoordinates(map.Grid.Owner, x + 0.5f, y + 0.5f));
                    Assert.That(em.GetComponent<TransformComponent>(piece).Anchored, Is.True);
                    pieces.Add(piece);
                }
            }

            root = pieces[0];
            farPiece = pieces[5];

            // Beside the far end of the table: three tiles from the root piece, one from the nearest.
            alice = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 3.5f, 0.5f));
            bob = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 3.5f, 1.5f));

            foreach (var piece in pieces)
            {
                Assert.That(em.System<WGPoolTableSystem>().GetRoot((piece, em.GetComponent<WGPoolTableComponent>(piece))),
                    Is.EqualTo(root), "Every piece should lead to the bottom left one.");
            }

            em.EventBus.RaiseLocalEvent(farPiece, new ActivateInWorldEvent(alice, farPiece, true));
            em.EventBus.RaiseLocalEvent(farPiece, new ActivateInWorldEvent(bob, farPiece, true));
            Assert.That(ui.IsUiOpen(root, WGPoolUiKey.Key, alice), Is.True);
            Assert.That(ui.IsUiOpen(root, WGPoolUiKey.Key, bob), Is.True);
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var table = em.GetComponent<WGPoolTableComponent>(root);
            Assert.That(ui.IsUiOpen(root, WGPoolUiKey.Key, alice), Is.True, "The window should stay open at the far end of the table.");
            Assert.That(table.Game, Is.Not.Null);
            var game = table.Game!;

            void Send(EntityUid actor, BoundUserInterfaceMessage message)
            {
                message.Actor = actor;
                ui.RaiseUiMessage(root, WGPoolUiKey.Key, message);
            }

            Send(alice, new PoolShootMessage(0f, 1f));
            Assert.That(game.ShotId, Is.EqualTo(0), "A spectator shot.");

            Send(alice, new PoolSeatMessage(0));
            Send(bob, new PoolSeatMessage(0));
            Assert.That(table.Seats[0], Is.EqualTo(alice));
            Assert.That(table.Seats[1], Is.Null, "Bob took a side that was taken.");

            Send(alice, new PoolShootMessage(0f, 1f));
            Assert.That(game.ShotId, Is.EqualTo(0), "Alice shot without a cue.");

            var cue = em.SpawnEntity("WGPoolCue", em.GetComponent<TransformComponent>(alice).Coordinates);
            Assert.That(em.System<SharedHandsSystem>().TryPickupAnyHand(alice, cue), Is.True);

            Send(alice, new PoolPlaceCueMessage(new Vector2(10f, 10f)));
            Assert.That(game.Pos[PoolRules.CueBall], Is.EqualTo(new Vector2(10f, 10f)));
            Send(alice, new PoolPlaceCueMessage(new Vector2(60f, 10f)));
            Assert.That(game.Pos[PoolRules.CueBall], Is.EqualTo(new Vector2(10f, 10f)), "The break is taken from behind the line.");

            Send(alice, new PoolShootMessage(0.3f, 1f));
            Assert.That(game.ShotId, Is.EqualTo(1));
            Assert.That(ui.TryGetUiState<PoolUiState>(root, WGPoolUiKey.Key, out var state), Is.True);
            Assert.That(state!.ShotId, Is.EqualTo(1));
            Assert.That(state.Seats[0], Is.EqualTo(em.GetNetEntity(alice)));

            Send(alice, new PoolShootMessage(0.3f, 1f));
            Assert.That(game.ShotId, Is.EqualTo(1), "The balls were still rolling.");

            Send(alice, new PoolRerackMessage());
            Assert.That(table.Game, Is.SameAs(game), "No new rack while the balls roll.");

            Send(bob, new PoolSeatMessage(1));
            Assert.That(table.Seats[1], Is.EqualTo(bob));
            ui.CloseUi(root, WGPoolUiKey.Key, bob);
            Assert.That(table.Seats[1], Is.Null, "Closing the window gives the side up.");

            xforms.SetCoordinates(alice, new EntityCoordinates(map.Grid.Owner, 6.5f, 0.5f));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var table = em.GetComponent<WGPoolTableComponent>(root);
            Assert.That(ui.IsUiOpen(root, WGPoolUiKey.Key, alice), Is.False, "Alice walked away from the table.");
            Assert.That(table.Seats[0], Is.Null);
            Assert.That(table.Game, Is.Not.Null, "The game stays on the table.");
        });

        await pair.CleanReturnAsync();
    }
}
