using System.Linq;
using System.Numerics;
using Content.Server._WF.Encounters.Systems;
using Content.Server._WF.Lattice;
using Content.Server._WF.Remade.Corruption;
using Content.Server._WF.Remade.Monolith;
using Content.Server._WF.Remade.Siege;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._WF.Lattice;
using Content.Shared.Damage;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._WF.Remade;

/// <summary>A woken Scion's flesh ships come overgrown for the nearest anchor, bleed its shield, then its core, and board it.</summary>
public sealed class SiegeTest
{
    [Test]
    public async Task FleshShipsBesiegeAnAnchor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var config = server.ResolveDependency<IConfigurationManager>();
        var lattice = entities.System<WFLatticeSystem>();
        var siege = entities.System<WFSiegeSystem>();
        var maps = entities.System<SharedMapSystem>();
        var xforms = entities.System<SharedTransformSystem>();

        await server.WaitPost(() =>
        {
            // Waves only when asked; the shield bleeds faster than it mends, so one ship shows.
            config.SetCVar(WFSiegeCVars.FirstWave, 100000f);
            config.SetCVar(WFSiegeCVars.ShieldRate, 50000f);
            lattice.Setup(map.MapUid);
        });
        await pair.RunSeconds(2f);

        EntityUid ship = default;
        WFNecroticShipComponent necrotic = default!;
        await server.WaitAssertion(() =>
        {
            var hull = maps.CreateGridEntity(map.MapId);
            xforms.SetWorldPosition(hull.Owner, new Vector2(9000f, 0f));
            maps.SetTile(hull.Owner, hull.Comp, Vector2i.Zero, map.Tile.Tile);
            var scion = entities.SpawnEntity("WFMonolithScion", new EntityCoordinates(hull.Owner, new Vector2(0.5f, 0.5f)));
            var monolith = entities.GetComponent<WFMonolithComponent>(scion);
            monolith.Resonance = 60f;

            Assert.That(siege.SendWave((scion, monolith), map.MapUid), Is.EqualTo(1), "a Scion at Harvest sent no flesh ship.");
            (ship, necrotic) = entities.EntityQuery<WFNecroticShipComponent>().Select(c => (c.Owner, c)).Single();
            Assert.That(entities.GetComponent<WFLatticeAnchorComponent>(necrotic.Core).Primary, Is.True, "a flesh ship isn't after an anchor.");
            Assert.That(entities.EntityQuery<WFCorruptionComponent>().Any(c => entities.GetComponent<TransformComponent>(c.Owner).GridUid == ship),
                Is.True, "a flesh ship came without its flesh.");

            // The Choir crew count the Remade they carry as their own, not as boarders.
            var crewman = entities.EntityQuery<Content.Server._WF.NpcCrew.Components.WFCrewComponent>()
                .First(c => entities.GetComponent<TransformComponent>(c.Owner).GridUid == ship).Owner;
            var passenger = entities.EntityQuery<Content.Server._WF.Encounters.Components.WFEncounterPassengerComponent>()
                .First(p => entities.GetComponent<TransformComponent>(p.Owner).GridUid == ship).Owner;
            Assert.That(entities.System<Content.Server._WF.NpcCrew.Systems.WFCrewSecuritySystem>().IsAuthorized(crewman, passenger), Is.True,
                "a flesh ship's crew treat its own Remade as boarders.");

            // Bring it alongside its anchor.
            var core = xforms.GetWorldPosition(necrotic.Core);
            xforms.SetWorldPosition(ship, core + Vector2.Normalize(core) * 500f);
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var emitter = Emitter(entities, necrotic.Station);
            Assert.That(emitter.Damage, Is.GreaterThan(0f), "a flesh ship alongside didn't bleed the anchor's shield.");

            // Bring the shield down.
            emitter.OverloadAccumulator = 100000f;
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<DamageableComponent>(necrotic.Core).TotalDamage.Float(), Is.GreaterThan(0f),
                "a flesh ship didn't bleed an unshielded anchor core.");
            Assert.That(necrotic.Boarders, Is.Not.Empty, "a flesh ship put nobody aboard an unshielded anchor.");
            Assert.That(entities.GetComponent<MetaDataComponent>(necrotic.Boarders[0]).EntityPrototype?.ID, Does.StartWith("WFRemade"),
                "a boarder isn't Remade.");
            Assert.That(entities.GetComponent<TransformComponent>(necrotic.Boarders[0]).GridUid, Is.EqualTo(necrotic.Station),
                "a boarder didn't land on the anchor station.");

            config.SetCVar(WFSiegeCVars.FirstWave, WFSiegeCVars.FirstWave.DefaultValue);
            config.SetCVar(WFSiegeCVars.ShieldRate, WFSiegeCVars.ShieldRate.DefaultValue);
            entities.System<WFEncounterSystem>().End(necrotic.Encounter);
        });

        await pair.CleanReturnAsync();
    }

    private static ShipShieldEmitterComponent Emitter(IEntityManager entities, EntityUid station)
    {
        return entities.EntityQuery<WFLatticeAnchorShieldComponent>()
            .Select(shield => shield.Owner)
            .Where(uid => entities.GetComponent<TransformComponent>(uid).GridUid == station)
            .Select(entities.GetComponent<ShipShieldEmitterComponent>)
            .First();
    }
}
