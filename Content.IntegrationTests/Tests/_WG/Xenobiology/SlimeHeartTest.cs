#nullable enable
using System.Numerics;
using Content.IntegrationTests.Pair;
using System.Linq;
using Content.Server._WG.Xenobiology;
using Content.Server.Destructible;
using Content.Shared._WG.Xenobiology;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Xenobiology;

/// <summary>
/// A slime heart regrows the damaged parts of the hull it is bolted to while it is fed, one heart to a hull.
/// </summary>
[TestFixture]
[TestOf(typeof(WGSlimeHeartSystem))]
public sealed class SlimeHeartTest
{
    private const string Heart = "WGSlimeHeartAdamantine";
    private const string Extract = "AdamantineSlimeExtract";
    private const string Wall = "WallPlastitanium";
    private static readonly ProtoId<DamageTypePrototype> Structural = "Structural";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private static EntityUid CreateHull(TestPair pair, MapId map, Vector2 pos)
    {
        var server = pair.Server;
        var maps = server.System<SharedMapSystem>();
        var grid = maps.CreateGridEntity(map);
        var plating = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        for (var x = 0; x < 4; x++)
        {
            for (var y = 0; y < 4; y++)
            {
                maps.SetTile(grid, new Vector2i(x, y), plating);
            }
        }

        server.System<SharedTransformSystem>().SetWorldPosition(grid, pos);
        return grid;
    }

    private static EntityCoordinates At(EntityUid hull, int x, int y)
    {
        return new EntityCoordinates(hull, x + 0.5f, y + 0.5f);
    }

    private const string RegrowEffect = "WGEffectSlimeRegrowAdamantine";

    private static WGSlimeHeartState State(TestPair pair, EntityUid heart)
    {
        pair.Server.System<SharedAppearanceSystem>().TryGetData<WGSlimeHeartState>(heart, WGSlimeHeartVisuals.State, out var state);
        return state;
    }

    private static int Effects(TestPair pair, MapId map)
    {
        var entMan = pair.Server.EntMan;
        return entMan.AllEntities<MetaDataComponent>()
            .Count(e => e.Comp.EntityPrototype?.ID == RegrowEffect && entMan.GetComponent<TransformComponent>(e).MapID == map);
    }

    private static bool Refused(TestPair pair, EntityUid uid)
    {
        var ev = new AnchorAttemptEvent(uid, uid);
        pair.Server.EntMan.EventBus.RaiseLocalEvent(uid, ev);
        return ev.Cancelled;
    }

    [Test]
    public async Task RegrowsQuietArmorWhileFed()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hearts = server.System<WGSlimeHeartSystem>();
            var damageable = server.System<DamageableSystem>();
            var xforms = server.System<SharedTransformSystem>();
            var protos = server.ResolveDependency<IPrototypeManager>();

            var hull = CreateHull(pair, map.MapId, new Vector2(40, 40));
            var wall = entMan.SpawnEntity(Wall, At(hull, 0, 0));
            var heart = entMan.SpawnEntity(Heart, At(hull, 2, 2));
            var comp = entMan.GetComponent<WGSlimeHeartComponent>(heart);
            var damage = entMan.GetComponent<DamageableComponent>(wall);
            // Not just armor: windows and thrusters regrow too.
            var others = new[] { entMan.SpawnEntity("Window", At(hull, 3, 0)), entMan.SpawnEntity("Thruster", At(hull, 0, 3)) };

            // Far more than one beat heals, so the share healed is not a round two-decimal ratio of it.
            damageable.TryChangeDamage(wall, new DamageSpecifier(protos.Index(Structural), 700), true);
            var hurt = damage.TotalDamage;
            Assert.That(hurt, Is.GreaterThan(FixedPoint2.Zero));
            var othersHurt = new FixedPoint2[others.Length];
            for (var i = 0; i < others.Length; i++)
            {
                Assert.That(entMan.GetComponent<TransformComponent>(others[i]).Anchored);
                damageable.TryChangeDamage(others[i], new DamageSpecifier(protos.Index(Blunt), 20), true);
                othersHurt[i] = entMan.GetComponent<DamageableComponent>(others[i]).TotalDamage;
                Assert.That(othersHurt[i], Is.GreaterThan(FixedPoint2.Zero));
            }

            // Loose, it does nothing.
            hearts.Tick((heart, comp));
            Assert.That(damage.TotalDamage, Is.EqualTo(hurt));

            // A new heart starts unfed: bolted down, it stays dormant until it eats.
            xforms.AnchorEntity(heart);
            Assert.That(hearts.IsBeating((heart, comp)), Is.False);
            hearts.Tick((heart, comp));
            Assert.That(damage.TotalDamage, Is.EqualTo(hurt));
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.IsBeating((heart, comp)));

            // Fresh damage waits out the quiet delay.
            hearts.Tick((heart, comp));
            hearts.Tick((heart, comp));
            Assert.That(damage.TotalDamage, Is.EqualTo(hurt));
            Assert.That(State(pair, heart), Is.EqualTo(WGSlimeHeartState.Beating));
            Assert.That(Effects(pair, map.MapId), Is.Zero);

            // Mending shows on the heart and on the wall.
            comp.QuietDelay = TimeSpan.Zero;
            hearts.Tick((heart, comp));
            var mended = damage.TotalDamage;
            var beat = server.System<DestructibleSystem>().DestroyedAt(wall) * comp.RegrowFraction;
            Assert.That(beat, Is.LessThan(hurt));
            Assert.That(hurt - mended, Is.EqualTo(beat), "A beat heals its full share.");
            Assert.That(State(pair, heart), Is.EqualTo(WGSlimeHeartState.Mending));
            for (var i = 0; i < others.Length; i++)
            {
                Assert.That(entMan.GetComponent<DamageableComponent>(others[i]).TotalDamage, Is.LessThan(othersHurt[i]),
                    entMan.GetComponent<MetaDataComponent>(others[i]).EntityPrototype?.ID);
            }

            Assert.That(Effects(pair, map.MapId), Is.EqualTo(1 + others.Length));

            // More damage is new damage, and waits again.
            comp.QuietDelay = TimeSpan.FromSeconds(60);
            damageable.TryChangeDamage(wall, new DamageSpecifier(protos.Index(Structural), 50), true);
            hurt = damage.TotalDamage;
            hearts.Tick((heart, comp));
            hearts.Tick((heart, comp));
            Assert.That(damage.TotalDamage, Is.EqualTo(hurt));
            Assert.That(State(pair, heart), Is.EqualTo(WGSlimeHeartState.Beating));

            // Starved, it goes dormant.
            hurt = damage.TotalDamage;
            comp.FedUntil = TimeSpan.Zero;
            hearts.Tick((heart, comp));
            hearts.Tick((heart, comp));
            Assert.Multiple(() =>
            {
                Assert.That(hearts.IsBeating((heart, comp)), Is.False);
                Assert.That(damage.TotalDamage, Is.EqualTo(hurt));
                Assert.That(State(pair, heart), Is.EqualTo(WGSlimeHeartState.Dormant));
            });

            // An extract of its breed wakes it; a full heart refuses more.
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.IsBeating((heart, comp)));
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity(Extract, At(hull, 1, 1))), Is.False);
            Assert.That(hearts.TryFeed((heart, comp), entMan.SpawnEntity("GreySlimeExtract", At(hull, 1, 1))), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The adamantine extract's blood reaction gives a slimy flatpack that holds the heart.</summary>
    [Test]
    public async Task ExtractGrowsHeartFlatpack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var extract = entMan.SpawnEntity(Extract, map.GridCoords);
            var solution = new Solution("Blood", FixedPoint2.New(5));
            entMan.System<ReactiveSystem>().DoEntityReaction(extract, solution, ReactionMethod.Injection);
            var pack = entMan.EntityQuery<FlatpackComponent>()
                .Where(f => entMan.GetComponent<TransformComponent>(f.Owner).MapID == map.MapId)
                .ToList();
            Assert.That(pack, Has.Count.EqualTo(1));
            Assert.That(pack[0].Entity?.Id, Is.EqualTo(Heart));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OneHeartPerHull()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var xforms = server.System<SharedTransformSystem>();
            var hull = CreateHull(pair, map.MapId, new Vector2(40, 40));
            var first = entMan.SpawnEntity(Heart, At(hull, 0, 0));
            var second = entMan.SpawnEntity(Heart, At(hull, 2, 2));

            Assert.That(Refused(pair, second), Is.False);
            xforms.AnchorEntity(first);
            Assert.That(Refused(pair, second));
            xforms.Unanchor(first);
            Assert.That(Refused(pair, second), Is.False);

            // Two wrenched at once: the second to finish comes loose again.
            xforms.AnchorEntity(first);
            xforms.AnchorEntity(second);
            entMan.EventBus.RaiseLocalEvent(second, new UserAnchoredEvent(second, second));
            Assert.That(entMan.GetComponent<TransformComponent>(second).Anchored, Is.False);

            // Two that meet anyway (a merged hull section): only one fed heart works.
            xforms.AnchorEntity(second);
            var hearts = server.System<WGSlimeHeartSystem>();
            Entity<WGSlimeHeartComponent> a = (first, entMan.GetComponent<WGSlimeHeartComponent>(first));
            Entity<WGSlimeHeartComponent> b = (second, entMan.GetComponent<WGSlimeHeartComponent>(second));
            Assert.That(hearts.TryFeed(a, entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.TryFeed(b, entMan.SpawnEntity(Extract, At(hull, 1, 1))));
            Assert.That(hearts.IsOutbeaten(a) ^ hearts.IsOutbeaten(b), "Exactly one of the two must stand idle.");
        });

        await pair.CleanReturnAsync();
    }
}
