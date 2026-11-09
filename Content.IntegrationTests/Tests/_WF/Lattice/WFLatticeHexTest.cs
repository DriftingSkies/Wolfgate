using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server.Cargo.Components;
using Content.Server.Cargo.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared._WF.Lattice;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>
/// The hex field: it turns a Scion back at a standing beam and lets it through a broken one, shelters what is inside while
/// sealed, keeps a Scion from jumping in; anchors keep corruption off; a Scion sells only on a main station; and losing
/// every primary anchor unstrings the Lattice.
/// </summary>
public sealed class WFLatticeHexTest
{
    [Test]
    public async Task HexFieldScionAndEnd()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var lattice = entities.System<WFLatticeSystem>();
        var corruption = entities.System<WFCorruptionSystem>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();
        var protos = server.ResolveDependency<IPrototypeManager>();

        var config = server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();
        await server.WaitPost(() =>
        {
            // Lost anchors let go at once here; the fade has its own test.
            config.SetCVar(WFLatticeEndgameCVars.AnchorFade, 0f);
            lattice.Setup(map.MapUid);
        });
        await pair.RunSeconds(2f);

        Entity<MapGridComponent> carrier = default;
        EntityUid scion = default;
        Vector2 midpoint = default;
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.Hex, Has.Count.EqualTo(6), "the hex doesn't run through the six primary anchors.");
            Assert.That(field.HexUp.All(up => up), Is.True, "a beam of the intact hex is down.");
            Assert.That(lattice.IsSealed(map.MapUid), Is.True);
            Assert.That(entities.EntityQuery<WFLatticeAnchorComponent>().Where(x => x.Primary)
                .All(x => entities.HasComponent<Content.Server.Warps.WarpPointComponent>(x.Owner)), Is.True, "an anchor isn't a ghost warp point.");
            Assert.That(lattice.IsShielded(new MapCoordinates(Vector2.Zero, map.MapId)), Is.True, "the middle of a sealed hex isn't sheltered.");
            Assert.That(lattice.IsShielded(new MapCoordinates(new Vector2(20000f, 0f), map.MapId)), Is.False, "deep space is sheltered.");
            Assert.That(corruption.IsSanctuary(map.Grid), Is.True, "a grid inside the sealed hex isn't a sanctuary.");

            midpoint = (field.Hex[0] + field.Hex[1]) / 2f;
            carrier = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(carrier.Owner, midpoint * 1.3f);
            maps.SetTile(carrier.Owner, carrier.Comp, Vector2i.Zero, map.Tile.Tile);
            scion = entities.SpawnEntity("WFMonolithScion", new EntityCoordinates(carrier.Owner, new Vector2(0.5f, 0.5f)));
            Assert.That(lattice.ScionMayJumpTo(carrier.Owner, new MapCoordinates(Vector2.Zero, map.MapId)), Is.False,
                "a ship carrying a Scion may jump inside the sealed hex.");
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => xforms.SetWorldPosition(carrier.Owner, midpoint * 0.8f));
        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(WFLatticeHex.Inside(field.Hex, xforms.GetWorldPosition(scion)), Is.False, "a Scion crossed a standing beam.");

            // Lose one anchor: its two beams fall and the hex shelters nothing.
            var corner = field.Hex[0];
            var core = entities.EntityQuery<WFLatticeAnchorComponent>()
                .First(anchor => anchor.Primary && Vector2.Distance(xforms.GetWorldPosition(anchor.Owner), corner) < 1f).Owner;
            entities.DeleteEntity(core);
        });

        await pair.RunSeconds(3f);
        Vector2 gap = default;
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(field.HexUp.Count(up => !up), Is.EqualTo(2), "a lost anchor didn't break its two beams.");
            Assert.That(lattice.IsSealed(map.MapUid), Is.False);
            // Only the broken slices open up; the rest of the ring still shelters.
            var down = field.HexUp.IndexOf(false);
            var up = field.HexUp.IndexOf(true);
            Vector2 InSlice(int i) => (field.Hex[i] + field.Hex[(i + 1) % field.Hex.Count]) / 2f * 0.6f;
            Assert.That(lattice.IsShielded(new MapCoordinates(InSlice(down), map.MapId)), Is.False, "a broken slice still shelters.");
            Assert.That(lattice.IsShielded(new MapCoordinates(InSlice(up), map.MapId)), Is.True, "a standing slice stopped sheltering.");
            Assert.That(lattice.ScionMayJumpTo(carrier.Owner, new MapCoordinates(Vector2.Zero, map.MapId)), Is.True);

            var broken = field.HexUp.IndexOf(false);
            gap = (field.Hex[broken] + field.Hex[(broken + 1) % field.Hex.Count]) / 2f;
            xforms.SetWorldPosition(carrier.Owner, gap * 1.3f);
        });

        await server.WaitRunTicks(5);
        await server.WaitPost(() => xforms.SetWorldPosition(carrier.Owner, gap * 0.8f));
        await server.WaitRunTicks(5);

        EntityUid sold = default, warded = default, unwarded = default, hiddenWard = default;
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            Assert.That(WFLatticeHex.Inside(field.Hex, xforms.GetWorldPosition(scion)), Is.True, "a Scion couldn't pass a broken beam.");

            // An anchor's ward keeps corruption off a grid near it and no further.
            var far = new Vector2(0f, -9000f);
            var ward = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(ward.Owner, far);
            maps.SetTile(ward.Owner, ward.Comp, Vector2i.Zero, map.Tile.Tile);
            entities.SpawnEntity("WFLatticeCentralAnchor", new EntityCoordinates(ward.Owner, new Vector2(0.5f, 0.5f)));
            warded = ward.Owner;

            // A station that hides from afar keeps its ward but shows no ring on radar.
            var hidden = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(hidden.Owner, far + new Vector2(0f, -4000f));
            maps.SetTile(hidden.Owner, hidden.Comp, Vector2i.Zero, map.Tile.Tile);
            entities.System<ShuttleSystem>().AddIFFFlag(hidden.Owner, IFFFlags.HideLabelAlways);
            entities.SpawnEntity("WFLatticeCentralAnchor", new EntityCoordinates(hidden.Owner, new Vector2(0.5f, 0.5f)));
            hiddenWard = hidden.Owner;

            var outside = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(outside.Owner, far + new Vector2(2000f, 0f));
            maps.SetTile(outside.Owner, outside.Comp, Vector2i.Zero, map.Tile.Tile);
            unwarded = outside.Owner;

            // Only a main station buys a Scion.
            var market = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(market.Owner, far - new Vector2(3000f, 0f));
            maps.SetTile(market.Owner, market.Comp, Vector2i.Zero, map.Tile.Tile);
            entities.AddComponent<WFRemadeSanctuaryComponent>(market.Owner);
            sold = entities.SpawnEntity("WFMonolithScion", new EntityCoordinates(market.Owner, new Vector2(0.5f, 0.5f)));
            entities.GetComponent<WFMonolithComponent>(scion).Resonance = 50f;
        });

        await pair.RunSeconds(6f);
        await server.WaitAssertion(() =>
        {
            Assert.That(corruption.IsWarded(warded), Is.True, "the central anchor doesn't keep corruption off its grid.");
            Assert.That(corruption.IsWarded(unwarded), Is.False, "an anchor's ward reaches 2 km.");
            Assert.That(corruption.IsWarded(hiddenWard), Is.True, "a hidden station's anchor keeps no corruption off.");
            var rings = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid).Wards;
            Assert.That(rings.Any(r => Vector2.Distance(r, xforms.GetWorldPosition(warded)) < 5f), Is.True, "a ward has no ring on radar.");
            Assert.That(rings.Any(r => Vector2.Distance(r, xforms.GetWorldPosition(hiddenWard)) < 5f), Is.False,
                "a hidden station's ward ring gives it away on radar.");
            Assert.That(entities.HasComponent<CargoSellBlacklistComponent>(sold), Is.False, "a Scion on a main station won't sell.");
            Assert.That(entities.HasComponent<CargoSellBlacklistComponent>(scion), Is.True, "a Scion sells away from a main station.");
            var price = entities.System<PricingSystem>().GetPrice(scion);
            Assert.That(price, Is.GreaterThanOrEqualTo(7_400_000), "a Scion at 50 resonance doesn't fetch more than a quiet one.");

            // Every primary anchor lost: the Unstringing.
            foreach (var anchor in entities.EntityQuery<WFLatticeAnchorComponent>().Where(a => a.Primary).ToList())
            {
                entities.DeleteEntity(anchor.Owner);
            }
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var field = entities.GetComponent<WFLatticeFieldComponent>(map.MapUid);
            var profile = protos.Index(field.Profile);
            Assert.That(field.Unstrung, Is.True, "losing every primary anchor didn't unstring the Lattice.");
            Assert.That(field.Anchors, Is.Empty, "an anchor still holds the field after the Unstringing.");
            Assert.That(lattice.BandAt(map.MapUid, Vector2.Zero), Is.EqualTo(profile.Bands.Count - 1), "the Coda isn't everywhere.");
            lattice.Tacet((map.MapUid, field));
            Assert.That(field.Tacet, Is.True);
            config.SetCVar(WFLatticeEndgameCVars.AnchorFade, WFLatticeEndgameCVars.AnchorFade.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A lost anchor's hold fades over time rather than going at once, so the bands creep inward.</summary>
    [Test]
    public async Task LostAnchorsFadeSlowly()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var lattice = entities.System<WFLatticeSystem>();
        var xforms = entities.System<SharedTransformSystem>();
        var config = server.ResolveDependency<Robust.Shared.Configuration.IConfigurationManager>();

        await server.WaitPost(() =>
        {
            config.SetCVar(WFLatticeEndgameCVars.AnchorFade, 100f);
            lattice.Setup(map.MapUid);
        });
        await pair.RunSeconds(2f);

        var corner = Vector2.Zero;
        var before = 0f;
        await server.WaitPost(() =>
        {
            var core = entities.EntityQuery<WFLatticeAnchorComponent>().First(a => a.Primary).Owner;
            corner = xforms.GetWorldPosition(core);
            before = lattice.FieldAt(map.MapUid, corner);
            entities.DeleteEntity(core);
        });
        await pair.RunSeconds(3f);
        await server.WaitAssertion(() =>
        {
            var after = lattice.FieldAt(map.MapUid, corner);
            Assert.That(after, Is.LessThan(before), "the lost anchor's hold didn't weaken at all.");
            Assert.That(before - after, Is.LessThan(0.2f), "the lost anchor's hold went at once instead of fading.");
            config.SetCVar(WFLatticeEndgameCVars.AnchorFade, WFLatticeEndgameCVars.AnchorFade.DefaultValue);
        });

        await pair.CleanReturnAsync();
    }
}
