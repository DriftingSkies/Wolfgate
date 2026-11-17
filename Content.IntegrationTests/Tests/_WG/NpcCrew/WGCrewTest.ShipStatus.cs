#nullable enable
using System.Numerics;
using Content.Server._WG.NpcCrew.Components;
using Content.Server._WG.NpcCrew.Systems;
using Content.Server.Shuttles.Components;
using Content.Shared._WG.NpcCrew;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.NpcCrew;

public sealed partial class WGCrewTest
{
    /// <summary>A ship is disabled once it loses its thrust, or everyone who was aboard, for 10 s; a bare hull never is.</summary>
    [Test]
    public async Task ShipIsDisabledOnceItLosesThrustAndWeapons()
    {
        var wreck = await CreateDeck(new Vector2(6, 0), 3, gravity: true);
        var ship = await CreateDeck(new Vector2(20, 0), 3, gravity: true);
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WGCrewShipStatusSystem>();
            SEntMan.EnsureComponent<ShuttleComponent>(wreck);
            SEntMan.EnsureComponent<ShuttleComponent>(ship).LinearThrust[0] = 10f;
            Assert.That(status.IsDisabled(wreck), Is.False, "A hull never seen working is not judged.");
            Assert.That(status.IsDisabled(ship), Is.False);
            SEntMan.GetComponent<ShuttleComponent>(ship).LinearThrust[0] = 0f;
        });
        await WaitUntil(() => Server.System<WGCrewShipStatusSystem>().IsDisabled(ship), 2400, () => "ship never counted as disabled");
        await Server.WaitAssertion(() =>
        {
            // The attacker's own rule decides: Destroy keeps firing on a crippled ship.
            var status = Server.System<WGCrewShipStatusSystem>();
            Assert.That(status.ShouldDisengage(wreck, ship), Is.True, "Disable is the default rule.");
            status.SetPolicy(wreck, WGCrewDisengage.Destroy, 500);
            Assert.That(status.ShouldDisengage(wreck, ship), Is.False);
        });
        var crewed = await CreateDeck(new Vector2(40, 0), 3, gravity: true);
        EntityUid hand = default;
        await Server.WaitAssertion(() =>
        {
            var status = Server.System<WGCrewShipStatusSystem>();
            Assert.That(status.IsDisabled(wreck), Is.False);
            SEntMan.EnsureComponent<ShuttleComponent>(crewed).LinearThrust[0] = 10f;
            hand = Server.System<WGCrewSystem>().SpawnCrewman(WGCrewRoles.Deckhand,
                new EntityCoordinates(crewed, new Vector2(1.5f)), "status")!.Value;
            SEntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(hand).Enabled = false;
            Assert.That(status.IsDisabled(crewed), Is.False);
            SEntMan.DeleteEntity(hand);
        });
        await WaitUntil(() => Server.System<WGCrewShipStatusSystem>().IsDisabled(crewed), 2400,
            () => "a ship that lost its whole crew never counted as disabled");
        await Server.WaitAssertion(() =>
        {
            // Destroy stops for a ship with nobody left alive; Deter also gives up on a distant one.
            var status = Server.System<WGCrewShipStatusSystem>();
            Assert.That(status.ShouldDisengage(wreck, crewed), Is.True, "Nobody is left alive aboard.");
            SEntMan.AddComponent<WGCrewHuntedHullComponent>(crewed);
            Assert.That(status.ShouldDisengage(wreck, crewed), Is.False, "Leaving a hunted hull doesn't call off the attack.");
            SEntMan.RemoveComponent<WGCrewHuntedHullComponent>(crewed);
            status.SetPolicy(ship, WGCrewDisengage.Deter, 5);
            Assert.That(status.ShouldDisengage(ship, wreck), Is.True, "Beyond the deterrence range.");
            Assert.That(status.ShouldDisengage(ship, wreck, range: false), Is.False);
            status.SetPolicy(ship, WGCrewDisengage.Deter, 500);
            Assert.That(status.ShouldDisengage(ship, wreck), Is.False);
        });
    }
}
