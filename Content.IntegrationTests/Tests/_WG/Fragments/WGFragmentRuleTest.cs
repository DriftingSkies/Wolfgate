using System.Linq;
using System.Numerics;
using Content.Server._WG.AdminVessels;
using Content.Server._WG.Fragments;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Explosion;
using Content.Shared.FixedPoint;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Content.Server._WG.Remade.Conversion;
using Content.Server._WG.Remade.Monolith;
using Content.Server.GameTicking;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WG.Fragments;

/// <summary>A fragment arrives with its heart and Remade and no Monolith, clears when the heart breaks and leaves with the rule.</summary>
[TestFixture]
public sealed class WGFragmentRuleTest
{
    [Test]
    public async Task FragmentHoldsAHeartAndNoScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var ticker = entMan.System<GameTicker>();
        EntityUid rule = default;

        await server.WaitPost(() => Assert.That(ticker.StartGameRule("WGFragmentEvent", out rule)));
        await pair.RunSeconds(45f);

        EntityUid grid = default;
        await server.WaitAssertion(() =>
        {
            var comp = entMan.GetComponent<WGFragmentRuleComponent>(rule);
            var maps = entMan.System<Robust.Shared.GameObjects.SharedMapSystem>();
            Assert.That(maps.MapExists(ticker.DefaultMap), $"no default map ({ticker.DefaultMap}); run level {ticker.RunLevel}");
            Assert.That(ticker.IsGameRuleActive(rule), "rule not active");
            Assert.That(comp.Grid, Is.Not.Null, "the fragment never arrived.");
            grid = comp.Grid!.Value;
            Assert.That(comp.HeartUid is { } heart && entMan.GetComponent<TransformComponent>(heart).GridUid == grid,
                "the heart isn't on the fragment.");
            Assert.That(entMan.EntityQuery<WGMonolithComponent>(), Is.Empty, "a fragment holds a Monolith.");
            Assert.That(comp.Remade.All(entMan.HasComponent<WGRemadeComponent>), Is.True);
            Assert.That(comp.Remade.Count, Is.GreaterThanOrEqualTo(comp.Garrison + comp.GarrisonElites),
                "the landing garrison is missing.");
            Assert.That(entMan.HasComponent<WGBlastproofFloorsComponent>(grid), "the fragment's floors aren't blast-proof.");
            Assert.That(entMan.EntityQuery<WGFragmentHullComponent>().Any(), "the fragment has no sealed hull.");

            entMan.System<Content.Shared.Destructible.SharedDestructibleSystem>().DestroyEntity(comp.HeartUid!.Value);
        });
        await pair.RunSeconds(2f);

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<WGFragmentRuleComponent>(rule).Cleared, Is.True, "breaking the heart didn't clear it.");
            ticker.EndGameRule(rule);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() => Assert.That(entMan.Deleted(grid) || entMan.IsQueuedForDeletion(grid), "the fragment outlived its rule."));
        await pair.CleanReturnAsync();
    }

    /// <summary>Only someone aboard the heart's grid and within reach can hurt it; blasts never do.</summary>
    [Test]
    public async Task HeartOnlyTakesHarmFromAboard()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var damageable = entMan.System<DamageableSystem>();
            var protoMan = server.ProtoMan;
            var blunt = new DamageSpecifier(protoMan.Index<DamageTypePrototype>("Blunt"), 50);

            var heart = entMan.SpawnEntity("WGFragmentHeart", map.GridCoords);
            var reach = entMan.GetComponent<WGFragmentHeartComponent>(heart).Reach;

            // Attackers are spawned on floor so they stay on the grid instead of dropping to the map.
            var maps = entMan.System<SharedMapSystem>();
            var farTile = new Vector2i((int) reach + 5, 0);
            maps.SetTile(map.Grid, farTile, map.Tile.Tile);
            var near = entMan.SpawnEntity(null, map.GridCoords);
            var far = entMan.SpawnEntity(null, maps.GridTileToLocal(map.Grid, map.Grid, farTile));
            var offGrid = entMan.SpawnEntity(null, new MapCoordinates(map.MapCoords.Position + new Vector2(0f, 50f), map.MapId));
            Assert.That(entMan.GetComponent<TransformComponent>(far).GridUid, Is.EqualTo(map.Grid.Owner));
            Assert.That(entMan.GetComponent<TransformComponent>(offGrid).GridUid, Is.Null);

            FixedPoint2 Total() => entMan.GetComponent<DamageableComponent>(heart).TotalDamage;

            Assert.Multiple(() =>
            {
                damageable.TryChangeDamage(heart, blunt, origin: null);
                Assert.That(Total(), Is.EqualTo(FixedPoint2.Zero), "harm with no attacker landed.");
                damageable.TryChangeDamage(heart, blunt, origin: offGrid);
                Assert.That(Total(), Is.EqualTo(FixedPoint2.Zero), "harm from off the fragment landed.");
                damageable.TryChangeDamage(heart, blunt, origin: far);
                Assert.That(Total(), Is.EqualTo(FixedPoint2.Zero), "harm from out of reach landed.");
                damageable.TryChangeDamage(heart, blunt, origin: near);
                Assert.That(Total(), Is.GreaterThan(FixedPoint2.Zero), "harm from aboard and in reach was dropped.");
            });

            var blast = new GetExplosionResistanceEvent("Default");
            entMan.EventBus.RaiseLocalEvent(heart, ref blast);
            Assert.That(blast.DamageCoefficient, Is.Zero, "blasts still hurt the heart.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>A sealed fragment's walls take no harm from anyone and stop blasts; its floors survive them.</summary>
    [Test]
    public async Task SealedHullCannotBeBroken()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var wall = entMan.SpawnEntity("WallReinforcedChitinMonolithic", map.GridCoords);
            entMan.System<WGFragmentRuleSystem>().SealHull(map.Grid);

            Assert.That(entMan.HasComponent<WGBlastproofFloorsComponent>(map.Grid), "the floors aren't blast-proof.");
            Assert.That(entMan.HasComponent<WGFragmentHullComponent>(wall), "the wall wasn't sealed.");

            var structural = new DamageSpecifier(server.ProtoMan.Index<DamageTypePrototype>("Structural"), 1000000);
            entMan.System<DamageableSystem>().TryChangeDamage(wall, structural, ignoreResistances: true, origin: wall);
            Assert.That(entMan.GetComponent<DamageableComponent>(wall).TotalDamage, Is.EqualTo(FixedPoint2.Zero),
                "a sealed wall took damage.");

            var blast = new GetExplosionResistanceEvent("Default");
            entMan.EventBus.RaiseLocalEvent(wall, ref blast);
            Assert.That(blast.DamageCoefficient, Is.Zero, "blasts still hurt a sealed wall.");
        });
        await pair.CleanReturnAsync();
    }
}
