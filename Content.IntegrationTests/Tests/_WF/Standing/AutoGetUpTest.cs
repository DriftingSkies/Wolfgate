#nullable enable
using System;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server._WF.Standing;
using Content.Shared._White.Standing;
using Content.Shared.CCVar;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Standing;

/// <summary>
/// Whether a body gets up by itself after a knockdown is its own player's setting, and a body with no player always
/// does. A client that only sees the knockdown, or names a body that is not its own, has no say.
/// </summary>
[TestFixture]
[TestOf(typeof(AutoGetUpSystem))]
public sealed class AutoGetUpTest : GameTest
{
    private static readonly TimeSpan Knockdown = TimeSpan.FromSeconds(1);

    private StandingState State(EntityUid body) => SEntMan.GetComponent<StandingStateComponent>(body).CurrentState;

    /// <summary>Sets the test client's setting and waits until the server has it.</summary>
    private async Task SetClientAutoGetUp(bool value)
    {
        await OverrideCVar(Side.Client, CCVars.AutoGetUp, value);
        await RunTicksSync(5);
        var seen = !value;
        await Server.WaitPost(() => seen = Server.ResolveDependency<INetConfigurationManager>()
            .GetClientCVar(ServerSession!.Channel, CCVars.AutoGetUp));
        Assert.That(seen, Is.EqualTo(value), "the server never got the client's setting, so the test proves nothing.");
    }

    /// <summary>Knocks the bodies down, checks they are on the floor, then waits out the knockdown and the stand-up.</summary>
    private async Task KnockDown(Func<Task>? during, params EntityUid[] bodies)
    {
        await Server.WaitAssertion(() =>
        {
            foreach (var body in bodies)
                Assert.That(SEntMan.System<SharedStunSystem>().TryKnockdown(body, Knockdown, true), Is.True);
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            foreach (var body in bodies)
                Assert.That(State(body), Is.EqualTo(StandingState.Lying), "the knockdown did not put the body on the floor.");
        });

        if (during != null)
            await during();

        var standUp = 0f;
        await Server.WaitPost(() => standUp = SEntMan.GetComponent<LayingDownComponent>(bodies[0]).StandingUpTime);
        await RunSeconds((float) Knockdown.TotalSeconds + standUp + 1f);
    }

    [Test]
    public async Task OnlookerHasNoSayTest()
    {
        Assert.That(ServerSession, Is.Not.Null, "This test needs a connected pair.");
        var map = await WolfmedGameTest.CreateTestMap(Pair);
        var minds = SEntMan.System<SharedMindSystem>();
        EntityUid own = default, playerless = default;

        await Server.WaitPost(() =>
        {
            var session = ServerSession!;
            minds.WipeMind(session.ContentData()?.Mind);
            own = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            playerless = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            minds.TransferTo(minds.CreateMind(session.UserId).Owner, own);
        });
        await RunTicksSync(30);
        Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(own), "the player did not attach to its body.");

        // The player has it off: its own body stays down, and the body it only watches still gets up.
        await SetClientAutoGetUp(false);
        await KnockDown(null, own, playerless);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(State(own), Is.EqualTo(StandingState.Lying), "a body got up with its player's setting off.");
                Assert.That(State(playerless), Is.EqualTo(StandingState.Standing),
                    "a body with no player stayed down: the onlooker's setting was written on it.");
            });
        });

        // A client naming a body that is not its own changes nothing.
        var net = SEntMan.GetNetEntity(playerless);
        await KnockDown(async () =>
        {
            await Client.WaitPost(() => CEntMan.EntityNetManager.SendSystemNetworkMessage(new CheckAutoGetUpEvent(net)));
            await RunTicksSync(5);
        }, playerless);
        await Server.WaitAssertion(() =>
            Assert.That(State(playerless), Is.EqualTo(StandingState.Standing), "a client set another body's auto get up."));

        // The player turns it on: its own body gets up too.
        await SetClientAutoGetUp(true);
        await KnockDown(null, own, playerless);
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(State(own), Is.EqualTo(StandingState.Standing), "a body stayed down with its player's setting on.");
                Assert.That(State(playerless), Is.EqualTo(StandingState.Standing));
            });
        });
    }

    /// <summary>
    /// A player who takes a body part way through its knockdown is asked from then on, and a body its player leaves
    /// part way through gets up like any other body with no player.
    /// </summary>
    [Test]
    public async Task PlayerComesAndGoesMidKnockdownTest()
    {
        Assert.That(ServerSession, Is.Not.Null, "This test needs a connected pair.");
        var map = await WolfmedGameTest.CreateTestMap(Pair);
        var minds = SEntMan.System<SharedMindSystem>();
        EntityUid body = default, mind = default;
        StandingState tookOver = default, left = default;

        await Server.WaitPost(() =>
        {
            var session = ServerSession!;
            minds.WipeMind(session.ContentData()?.Mind);
            body = SEntMan.SpawnEntity("MobHuman", map.GridCoords);
            mind = minds.CreateMind(session.UserId).Owner;
        });
        await RunTicksSync(30);
        await SetClientAutoGetUp(false);

        // Nobody is in the body when it falls; the player, with the setting off, takes it before the knockdown ends.
        await KnockDown(async () =>
        {
            await Server.WaitPost(() => minds.TransferTo(mind, body));
            Assert.That(ServerSession!.AttachedEntity, Is.EqualTo(body), "the player did not attach to the body.");
        }, body);
        await Server.WaitPost(() => tookOver = State(body));

        // The player is in the body when it falls again, and leaves it before the knockdown ends.
        await KnockDown(async () =>
        {
            await Server.WaitPost(() => minds.TransferTo(mind, null));
            Assert.That(ServerSession!.AttachedEntity, Is.Not.EqualTo(body), "the player did not leave the body.");
        }, body);
        await Server.WaitPost(() => left = State(body));

        Assert.Multiple(() =>
        {
            Assert.That(tookOver, Is.EqualTo(StandingState.Lying), "a body got up with its new player's setting off.");
            Assert.That(left, Is.EqualTo(StandingState.Standing), "a body stayed down after its player left it.");
        });
    }
}
