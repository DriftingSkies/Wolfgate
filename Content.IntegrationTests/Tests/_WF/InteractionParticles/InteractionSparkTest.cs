#nullable enable
using Content.Server.Explosion.EntitySystems;
using Content.Shared._WF.InteractionParticles;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.IntegrationTests.Tests._WF.InteractionParticles;

/// <summary>
/// The spark effects the interaction particle port adds really spawn from triggers, item toggles and the bluespace
/// flash.
/// </summary>
[TestFixture]
[TestOf(typeof(Content.Shared._ES.Sparks.ESSparksSystem))]
public sealed class InteractionSparkTest
{
    [Test]
    public async Task SparksComeFromTriggersTogglesAndTheBluespaceFlash()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;

        var map = await pair.CreateTestMap();

        EntityUid igniter = default;
        EntityUid welder = default;

        await server.WaitPost(() =>
        {
            igniter = server.EntMan.SpawnEntity("Igniter", map.GridCoords);
            welder = server.EntMan.SpawnEntity("Welder", map.GridCoords);
        });

        await pair.RunTicksSync(2);

        // An igniter sparks through ESSparkOnTrigger, which the server-only trigger event feeds.
        Assert.That(CountProto(server, "ESEffectSparks"), Is.EqualTo(0), "an idle igniter already sparked");
        await server.WaitPost(() => server.System<TriggerSystem>().Trigger(igniter));
        await pair.RunTicksSync(2);
        Assert.That(CountProto(server, "ESEffectSparks"), Is.GreaterThan(0), "the trigger did not spark");

        // Toggling a welder on sparks through ESSparkOnItemToggle.
        Assert.That(CountProto(server, "WFEffectSparksSilent"), Is.EqualTo(0), "an idle welder already sparked");
        await server.WaitPost(() => server.System<ItemToggleSystem>().TryActivate(welder));
        await pair.RunTicksSync(2);
        Assert.That(CountProto(server, "WFEffectSparksSilent"), Is.GreaterThan(0), "toggling the welder did not spark");

        // The bluespace flash sparks on spawn and is blacklisted from the portals it spawns on.
        EntityUid flash = default;
        await server.WaitPost(() => flash = server.EntMan.SpawnEntity("EffectFlashBluespace", map.GridCoords));
        await pair.RunTicksSync(2);
        Assert.That(CountProto(server, "WFEffectSparksBluespace"), Is.GreaterThan(0), "the bluespace flash did not spark");
        Assert.That(server.EntMan.HasComponent<PortalBlacklistComponent>(flash), Is.True,
            "the bluespace flash can be teleported by the portal it spawned on");

        await pair.CleanReturnAsync();
    }

    private static int CountProto(ServerIntegrationInstance server, string id)
    {
        var count = 0;
        var query = server.EntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out _, out var meta))
        {
            if (meta.EntityPrototype?.ID == id)
                count++;
        }

        return count;
    }
}
