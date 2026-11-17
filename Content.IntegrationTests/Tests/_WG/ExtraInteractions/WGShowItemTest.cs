#nullable enable
using Content.Server._WG.ExtraInteractions;
using Content.Shared._WG.ExtraInteractions;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using ClientShowItemSystem = Content.Client._WG.ExtraInteractions.WGShowItemSystem;

namespace Content.IntegrationTests.Tests._WG.ExtraInteractions;

/// <summary>Pointing at a carried item shows it in a bubble to nearby clients; an item that isn't carried is refused.</summary>
[TestFixture]
[TestOf(typeof(WGShowItemSystem))]
public sealed class WGShowItemTest
{
    [Test]
    public async Task ShowCarriedItemTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var hands = entMan.System<SharedHandsSystem>();
        var show = entMan.System<WGShowItemSystem>();

        EntityUid user = default, held = default, loose = default;
        await server.WaitAssertion(() =>
        {
            user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            held = entMan.SpawnEntity("Crowbar", map.GridCoords);
            loose = entMan.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(hands.TryPickup(user, held), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, user);

            Assert.That(show.IsCarried(user, held), Is.True);
            Assert.That(show.IsCarried(user, loose), Is.False);
            Assert.That(show.IsCarried(user, user), Is.False);
        });
        await pair.RunTicksSync(10);

        // An item on the floor is refused.
        await client.WaitPost(() => client.EntMan.RaisePredictiveEvent(new WGShowItemRequestEvent(client.EntMan.GetNetEntity(pair.ToClientUid(loose)))));
        await pair.RunTicksSync(10);
        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WGShowItemCooldownComponent>(user), Is.False, "Showed an item the user doesn't carry."));

        await client.WaitPost(() => client.EntMan.RaisePredictiveEvent(new WGShowItemRequestEvent(client.EntMan.GetNetEntity(pair.ToClientUid(held)))));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
            Assert.That(entMan.HasComponent<WGShowItemCooldownComponent>(user), Is.True, "The server refused a carried item."));
        await client.WaitAssertion(() =>
        {
            var bubbles = client.EntMan.System<ClientShowItemSystem>().Bubbles;
            Assert.That(bubbles.TryGetValue(pair.ToClientUid(user), out var bubble), Is.True, "The client got no bubble.");
            Assert.That(bubble.Item, Is.EqualTo(pair.ToClientUid(held)));
        });

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }
}
