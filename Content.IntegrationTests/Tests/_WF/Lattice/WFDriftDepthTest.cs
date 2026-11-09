using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.DroneRings;
using Content.Shared._WF.Lattice;
using Content.Shared.Alert;
using Content.Shared.Hands.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>
/// What a person feels against where they are: alone is worse, a grounding drug helps. A ship soaks Drift into its hull
/// in the Coda until a drydock cleans it; a drift logger counts readings; and a drifting mind sees warnings that aren't true.
/// </summary>
public sealed class WFDriftDepthTest
{
    [Test]
    public async Task FeltDriftResidueLoggerAndLies()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();
        var timing = server.ResolveDependency<IGameTiming>();
        var lattice = entities.System<WFLatticeSystem>();
        var transform = entities.System<SharedTransformSystem>();

        EntityUid mob = default, logger = default;
        await server.WaitAssertion(() =>
        {
            lattice.Setup(map.MapUid);
            entities.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(map.Grid);
            transform.SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            entities.EnsureComponent<WFDriftComponent>(map.Grid).Drift = 60f;
            logger = entities.SpawnEntity("WFDriftLogger", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(mob, logger), Is.True);
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            var ship = entities.GetComponent<WFDriftComponent>(map.Grid);
            var head = entities.GetComponent<WFDriftComponent>(mob);
            Assert.That(head.Felt, Is.GreaterThan(ship.Drift), "being alone out here didn't make it worse.");
            Assert.That(ship.Residue, Is.GreaterThan(0f), "the Coda soaked nothing into the hull.");
            Assert.That(entities.GetComponent<WFDriftLoggerComponent>(logger).Readings, Is.GreaterThan(0f), "the logger logged nothing in the Coda.");
            Assert.That(entities.System<PricingSystem>().GetPrice(logger), Is.GreaterThan(100), "the logger's readings aren't worth anything.");

            var grounded = entities.EnsureComponent<WFGroundedComponent>(mob);
            grounded.Until = timing.CurTime + System.TimeSpan.FromSeconds(60);
            grounded.Share = 0.5f;

            entities.System<WFDriftEffectsSystem>().Show(players.Sessions.Single(), mob, WFLatticeHallucinationKind.FalseAlert);
            var alerts = entities.System<AlertsSystem>();
            Assert.That(new[] { "WFFalseLowOxygen", "WFFalseLowPressure", "WFFalseHot" }.Any(id => alerts.IsShowingAlert(mob, id)), Is.True,
                "no false warning showed.");
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var ship = entities.GetComponent<WFDriftComponent>(map.Grid);
            Assert.That(entities.GetComponent<WFDriftComponent>(mob).Felt, Is.LessThan(ship.Drift * 0.7f), "the drug didn't keep Drift off.");

            // Back in anchored space, the hull still holds what soaked in, until it is cleaned.
            ship.Residue = 10f;
            ship.Drift = 0f;
            transform.SetWorldPosition(map.Grid.Owner, new Vector2(3000f, 3000f));
        });

        await pair.RunSeconds(9f);
        await server.WaitAssertion(() =>
        {
            var ship = entities.GetComponent<WFDriftComponent>(map.Grid);
            Assert.That(ship.Band, Is.EqualTo(0));
            Assert.That(ship.Drift, Is.GreaterThanOrEqualTo(10f), "the hull's soaked-in Drift faded on its own.");
            Assert.That(lattice.CleanDriftQuote(map.Grid), Is.EqualTo(1500));
            lattice.CleanDrift(map.Grid);
            Assert.That(ship.Residue, Is.Zero);
            Assert.That(lattice.CleanDriftQuote(map.Grid), Is.Zero);

            var alerts = entities.System<AlertsSystem>();
            Assert.That(new[] { "WFFalseLowOxygen", "WFFalseLowPressure", "WFFalseHot" }.Any(id => alerts.IsShowingAlert(mob, id)), Is.False,
                "the false warning never went away.");
        });

        await pair.CleanReturnAsync();
    }
}
