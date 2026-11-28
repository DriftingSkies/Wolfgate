#nullable enable annotations

using Content.Client._WG.Settlers;
using Content.Shared._WG.Settlers;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WG.Settlers;

/// <summary>A Settlers board in the world: seats, starting, moves from seated players only, and leaving keeps the game.</summary>
public sealed class SettlersBoardTest
{
    [Test]
    public async Task SeatsStartAndLeave()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var em = server.EntMan;
        var ui = em.System<SharedUserInterfaceSystem>();
        var xforms = em.System<SharedTransformSystem>();
        var examine = em.System<ExamineSystemShared>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        EntityUid board = default;
        EntityUid alice = default;
        EntityUid bob = default;
        EntityUid carol = default;

        await server.WaitAssertion(() =>
        {
            board = em.SpawnEntity("WGSettlersBoard", new EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            alice = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 1.5f, 0.5f));
            bob = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, 0.5f, 1.5f));
            carol = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid.Owner, -0.5f, 0.5f));

            foreach (var player in new[] { alice, bob, carol })
            {
                em.EventBus.RaiseLocalEvent(board, new ActivateInWorldEvent(player, board, true));
                Assert.That(ui.IsUiOpen(board, WGSettlersUiKey.Key, player), Is.True);
            }
        });

        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGSettlersBoardComponent>(board);
            Assert.That(comp.Game, Is.Not.Null);
            var game = comp.Game!;

            void Send(EntityUid actor, BoundUserInterfaceMessage message)
            {
                message.Actor = actor;
                ui.RaiseUiMessage(board, WGSettlersUiKey.Key, message);
            }

            Send(alice, new SettlersStartMessage());
            Assert.That(game.State.Phase, Is.EqualTo(SettlersPhase.Lobby), "A spectator started the game.");

            Send(alice, new SettlersSeatMessage(0));
            Send(bob, new SettlersSeatMessage(0));
            Assert.That(comp.Seats[0], Is.EqualTo(alice));
            Assert.That(comp.Seats[1], Is.Null, "Bob took a seat that was taken.");
            Send(bob, new SettlersSeatMessage(2));
            Assert.That(comp.Seats[2], Is.EqualTo(bob));

            string Plain(string key) => FormattedMessage.FromMarkupOrThrow(loc.GetString(key)).ToString();
            Assert.That(examine.GetExamineText(board, carol).ToString(), Does.Not.Contain(Plain("wg-settlers-examine-running")),
                "No game is being played yet.");

            Send(bob, new SettlersStartMessage());
            Assert.That(game.State.Phase, Is.EqualTo(SettlersPhase.SetupOutpost));
            Assert.That(game.State.Players[0].InGame && game.State.Players[2].InGame, Is.True);

            Assert.That(examine.GetExamineText(board, carol).ToString(), Does.Contain(Plain("wg-settlers-examine-running")),
                "Examining the board shows the game in progress.");

            Send(carol, new SettlersSeatMessage(1));
            Assert.That(comp.Seats[1], Is.Null, "Seat 1 isn't in this game.");

            var turn = game.State.Turn;
            var mover = turn == 0 ? alice : bob;
            var other = turn == 0 ? bob : alice;
            Send(other, new SettlersActionMessage(new SettlersAction(SettlersActionKind.BuildOutpost, 0)));
            Assert.That(game.State.VertexOwner[0], Is.EqualTo(-1), "Out of turn.");
            Send(carol, new SettlersActionMessage(new SettlersAction(SettlersActionKind.BuildOutpost, 0)));
            Assert.That(game.State.VertexOwner[0], Is.EqualTo(-1), "A spectator moved.");
            Send(mover, new SettlersActionMessage(new SettlersAction(SettlersActionKind.BuildOutpost, 0)));
            Assert.That(game.State.VertexOwner[0], Is.EqualTo(turn));

            Send(alice, new SettlersNewGameMessage());
            Assert.That(comp.Game, Is.SameAs(game), "No new game while another player is seated.");

            ui.CloseUi(board, WGSettlersUiKey.Key, bob);
            Assert.That(comp.Seats[2], Is.Null, "Closing the window gives up the seat.");
            Assert.That(game.State.Players[2].Present, Is.False);
            Assert.That(game.State.Players[2].InGame, Is.True, "The seat stays in the game.");

            Send(carol, new SettlersSeatMessage(2));
            Assert.That(comp.Seats[2], Is.EqualTo(carol), "A vacant seat can be taken over.");

            xforms.SetCoordinates(alice, new EntityCoordinates(map.Grid.Owner, 8.5f, 0.5f));
        });

        await pair.RunTicksSync(30);

        await server.WaitAssertion(() =>
        {
            var comp = em.GetComponent<WGSettlersBoardComponent>(board);
            Assert.That(ui.IsUiOpen(board, WGSettlersUiKey.Key, alice), Is.False, "Alice walked away.");
            Assert.That(comp.Seats[0], Is.Null);
            Assert.That(comp.Game!.State.Running, Is.True, "The game stays on the board.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ResourceIconsLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var sprites = client.EntMan.System<SpriteSystem>();

        await client.WaitAssertion(() =>
        {
            var error = sprites.GetFallbackState().Frame0;
            for (var resource = 0; resource < SettlersRules.ResourceCount; resource++)
            {
                Assert.That(SettlersArt.ResourceTexture(resource), Is.Not.SameAs(error), $"Resource {resource} has no icon.");
            }
        });

        await pair.CleanReturnAsync();
    }
}
