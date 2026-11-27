using Content.Server._WG.HullBond;
using Content.Shared.Construction.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.ShipShields;

/// <summary>Checks a ship takes one shield generator.</summary>
[TestFixture]
public sealed class WGShipShieldLimitTest
{
    /// <summary>Spawns a generator as a hull loaded from a save carries it, without map init, so it can share a hull with another.</summary>
    internal static EntityUid SpawnSavedGenerator(IEntityManager entities, string prototype, EntityCoordinates coordinates)
    {
        var uid = entities.CreateEntityUninitialized(prototype, coordinates);
        entities.InitializeAndStartEntity(uid, doMapInit: false);
        return uid;
    }

    [Test]
    public async Task SecondGeneratorWillNotBoltDownBesideTheFirst()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = false, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var transform = entities.System<SharedTransformSystem>();
            var first = entities.SpawnEntity("ShieldGeneratorSmall", map.GridCoords);
            var firstXform = entities.GetComponent<TransformComponent>(first);
            Assert.That(firstXform.Anchored, Is.True);

            // Unpacked from a flatpack or built from a board, it spawns bolted down and comes loose at once.
            var second = entities.SpawnEntity("WGShieldGeneratorDeflector", map.GridCoords);
            var secondXform = entities.GetComponent<TransformComponent>(second);
            Assert.That(secondXform.Anchored, Is.False, "A second generator must not bolt down beside the first.");
            Assert.That(entities.GetComponent<WGHullBondComponent>(second).Hull, Is.Null,
                "A generator turned away must not bond to the hull, or it could never be installed anywhere.");
            Assert.That(firstXform.Anchored, Is.True, "The installed generator stays.");

            var user = entities.SpawnEntity("MobHuman", map.GridCoords);
            var attempt = new AnchorAttemptEvent(user, user);
            entities.EventBus.RaiseLocalEvent(second, attempt);
            Assert.That(attempt.Cancelled, Is.True, "A wrench is refused before it starts.");
            transform.AnchorEntity((second, secondXform));
            Assert.That(secondXform.Anchored, Is.False, "Bolted down by anything else, it comes loose again.");

            var outpost = entities.SpawnEntity("ShieldGeneratorPOI", map.GridCoords);
            Assert.That(entities.GetComponent<TransformComponent>(outpost).Anchored, Is.True, "Outpost generators are not ship generators.");
            entities.DeleteEntity(outpost);

            transform.Unanchor(first, firstXform);
            attempt = new AnchorAttemptEvent(user, user);
            entities.EventBus.RaiseLocalEvent(second, attempt);
            Assert.That(attempt.Cancelled, Is.False, "With the first taken off, the second may go in.");
            transform.AnchorEntity((second, secondXform));
            Assert.That(secondXform.Anchored, Is.True);
            Assert.That(entities.GetComponent<WGHullBondComponent>(second).Hull, Is.Not.Null, "Bolted down alone, it bonds as usual.");
            transform.AnchorEntity((first, firstXform));
            Assert.That(firstXform.Anchored, Is.False, "Now the first is the one turned away.");
        });
        await pair.CleanReturnAsync();
    }
}
