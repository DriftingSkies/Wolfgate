using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Director;
using Content.Server._WF.Remade.Monolith;
using Content.Shared._WF.Lattice;
using Content.Shared.Alert;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>Drift in a person's head: fed by a source nearby, by a Monolith, and fading once nothing feeds it.</summary>
public sealed class WFDriftMindTest
{
    [Test]
    public async Task ASourceFeedsAHeadThatFadesWithoutIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        EntityUid matter = default;
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            matter = entities.SpawnEntity("WFMaterialExoticMatter", map.GridCoords.Offset(new Vector2(0.5f, 0f)));
        });

        await pair.RunSeconds(6f);
        var fed = 0f;
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<WFDriftComponent>(mob, out var drift), Is.True, "exotic matter put no Drift in a head beside it.");
            fed = drift!.Mind;
            Assert.That(fed, Is.GreaterThan(0.2f));
            Assert.That(drift.Flavour, Is.EqualTo(WFDriftFlavour.Unmooring));
            Assert.That(entities.System<AlertsSystem>().IsShowingAlert(mob, "WFDrift"), Is.True, "a head holding Drift shows no gauge.");
            entities.DeleteEntity(matter);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFDriftComponent>(mob).Mind, Is.LessThan(fed), "Drift in a head didn't fade once the source was gone.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A player on a ship in the Coda sees the scrambled gauge.</summary>
    [Test]
    public async Task TheCodaScramblesTheGauge()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeSystem>().Setup(map.MapUid);
            entities.EnsureComponent<Content.Server.Shuttles.Components.ShuttleComponent>(map.Grid);
            entities.System<SharedTransformSystem>().SetWorldPosition(map.Grid.Owner, new Vector2(72000f, 0f));
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<TransformComponent>(mob).GridUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(entities.TryGetComponent<WFDriftComponent>(map.Grid, out var ship), Is.True, "the ship holds no Drift.");
            Assert.That(ship!.Band, Is.EqualTo(4));
            var gauge = entities.System<AlertsSystem>().GetActiveAlerts(mob)!.Values.Single(alert => alert.Type == "WFDrift");
            Assert.That(gauge.Severity, Is.EqualTo((short) 21), "the gauge isn't scrambled in the Coda.");
        });

        // The client gets the scrambled state too, and draws it with the garbled dial.
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.ResolveDependency<IEntityManager>();
            var alerts = client.System<Content.Client.Alerts.ClientAlertsSystem>();
            var state = alerts.ActiveAlerts!.Values.Single(alert => alert.Type == "WFDrift");
            Assert.That(state.Severity, Is.EqualTo((short) 21), "the client's gauge isn't scrambled.");
            Assert.That(alerts.TryGet("WFDrift", out var proto), Is.True);
            var icon = (Robust.Shared.Utility.SpriteSpecifier.Rsi) proto!.GetIcon(state.Severity);
            Assert.That(icon.RsiState, Is.EqualTo("garbled"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A ship's Drift stops at its band's ceiling, and eases back down to it from above.</summary>
    [Test]
    public async Task EachBandHasACeiling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        await server.WaitAssertion(() =>
        {
            entities.System<WFLatticeSystem>().Setup(map.MapUid);
            // Untethered, whose ceiling is 65.
            entities.System<SharedTransformSystem>().SetWorldPosition(map.Grid.Owner, new Vector2(58000f, 0f));
            var mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            entities.EnsureComponent<WFDriftComponent>(map.Grid).Drift = 90f;
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            var drift = entities.GetComponent<WFDriftComponent>(map.Grid);
            Assert.That(drift.Band, Is.EqualTo(3));
            Assert.That(drift.Drift, Is.LessThan(90f).And.GreaterThan(65f), "Drift over the band's ceiling didn't ease down.");
            drift.Drift = 64.99f;
        });

        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<WFDriftComponent>(map.Grid).Drift, Is.EqualTo(65f).Within(0.01f), "Drift went past the band's ceiling.");
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AMonolithPutsItsDriftInAHead(bool choir)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        var players = server.ResolveDependency<IPlayerManager>();

        EntityUid mob = default;
        await server.WaitAssertion(() =>
        {
            mob = entities.SpawnEntity("MobHuman", map.GridCoords);
            players.SetAttachedEntity(players.Sessions.Single(), mob, force: true);
            if (choir)
                entities.EnsureComponent<WFChoirMemberComponent>(mob);

            var stone = entities.SpawnEntity("WFMonolithScion", map.GridCoords.Offset(new Vector2(3f, 0f)));
            entities.GetComponent<WFRemadeDirectorComponent>(stone).BuildUp = false;
            entities.RemoveComponent<WFRemadeDirectorComponent>(stone);
            entities.GetComponent<WFMonolithComponent>(stone).Resonance = 100f;
        });

        await pair.RunSeconds(8f);
        await server.WaitAssertion(() =>
        {
            if (choir)
            {
                Assert.That(entities.TryGetComponent<WFDriftComponent>(mob, out var held) ? held.Mind : 0f, Is.EqualTo(0f), "the stone put Drift in a Choir head.");
                Assert.That(entities.System<AlertsSystem>().IsShowingAlert(mob, "WFDrift"), Is.False, "a Choir member was shown the gauge.");
                return;
            }

            Assert.That(entities.TryGetComponent<WFDriftComponent>(mob, out var drift), Is.True, "a Crescendo beside a player put no Drift in their head.");
            Assert.That(drift!.Mind, Is.GreaterThan(3f));
            Assert.That(drift.MindLevel, Is.GreaterThan(0));
            Assert.That(drift.Flavour, Is.EqualTo(WFDriftFlavour.Monolith));
            var gauge = entities.System<AlertsSystem>().GetActiveAlerts(mob)!.Values.Single(alert => alert.Type == "WFDrift");
            Assert.That(gauge.Severity, Is.GreaterThanOrEqualTo((short) 22), "a Monolith's Drift didn't show on the red gauge.");
        });

        await pair.CleanReturnAsync();
    }
}
