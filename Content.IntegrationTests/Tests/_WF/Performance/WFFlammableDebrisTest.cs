using System.Collections.Generic;
using Content.IntegrationTests.Tests._WF.Wolfmed;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._WF.Performance;

/// <summary>Burning something to pieces that spawns more flammable things must not break the fire update.</summary>
[TestFixture]
[TestOf(typeof(FlammableSystem))]
public sealed class WFFlammableDebrisTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFFlammableDebris
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Appearance
  - type: Flammable
    fireSpread: false

- type: entity
  id: WFFlammableSplitter
  components:
  - type: Physics
    bodyType: Dynamic
  - type: Appearance
  - type: Damageable
  - type: Flammable
    fireSpread: false
    damage:
      types:
        Heat: 10
  - type: Destructible
    thresholds:
    - trigger:
        !type:DamageTrigger
        damage: 1
      behaviors:
      - !type:SpawnEntitiesBehavior
        spawn:
          WFFlammableDebris:
            min: 4
            max: 4
      - !type:DoActsBehavior
        acts: [ ""Destruction"" ]
";

    [Test]
    public async Task BurningIntoFlammableDebris()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var flammable = server.System<FlammableSystem>();
        var map = await pair.CreateTestMap();

        var splitters = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            server.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, WolfmedIvDripTest.StationAir());
            for (var i = 0; i < 8; i++)
            {
                var uid = entMan.SpawnEntity("WFFlammableSplitter", map.GridCoords);
                flammable.AdjustFireStacks(uid, 5f, entMan.GetComponent<FlammableComponent>(uid), ignite: true);
                splitters.Add(uid);
            }
        });

        // The fire update runs once a second per entity; give it a few.
        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in splitters)
            {
                Assert.That(entMan.Deleted(uid), "A burning splitter was never destroyed.");
            }

            var debris = 0;
            var query = entMan.EntityQueryEnumerator<FlammableComponent, MetaDataComponent>();
            while (query.MoveNext(out _, out _, out var meta))
            {
                if (meta.EntityPrototype?.ID == "WFFlammableDebris")
                    debris++;
            }

            Assert.That(debris, Is.EqualTo(splitters.Count * 4));
        });

        await pair.CleanReturnAsync();
    }
}
