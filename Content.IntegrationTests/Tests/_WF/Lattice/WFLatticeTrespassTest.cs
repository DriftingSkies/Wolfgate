using System.Linq;
using System.Numerics;
using Content.Server._WF.Lattice;
using Content.Shared._WF.Lattice;
using Content.Shared._WF.Remade;
using Content.Shared.Construction.Components;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WF.Lattice;

/// <summary>An anchor station names a player aboard it, and a Scion slows whoever drags it.</summary>
public sealed class WFLatticeTrespassTest
{
    [Test]
    public async Task TrespassAndHeavyScion()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var lattice = entities.System<WFLatticeSystem>();

        await server.WaitPost(() => lattice.Setup(map.MapUid));
        await pair.RunSeconds(2f);

        EntityUid station = default, mob = default;
        await server.WaitPost(() =>
        {
            station = entities.EntityQuery<WFLatticeAnchorStationComponent>().First().Owner;
            var core = entities.GetComponent<WFLatticeAnchorStationComponent>(station).Core;
            var at = entities.GetComponent<TransformComponent>(core).Coordinates.Offset(new Vector2(2f, 0f));
            mob = entities.SpawnEntity("MobHuman", at);
            server.PlayerMan.SetAttachedEntity(pair.Player!, mob);
        });

        await pair.RunSeconds(2f);
        await server.WaitAssertion(() =>
        {
            var comp = entities.GetComponent<WFLatticeAnchorStationComponent>(station);
            Assert.That(comp.Trespassers.ContainsKey(mob), Is.True, "a player aboard an anchor station wasn't named.");

            // On open deck, away from the station's walls.
            var hauler = entities.SpawnEntity("MobHuman", map.GridCoords);
            var scion = entities.SpawnEntity("WFMonolithScion", map.GridCoords);
            entities.System<SharedTransformSystem>().Unanchor(scion);
            var pulling = entities.System<PullingSystem>();
            Assert.That(pulling.TryStartPull(hauler, scion), Is.True);
            Assert.That(entities.TryGetComponent<WFHeavyPullingComponent>(hauler, out var heavy) && heavy.Multiplier < 0.5f, Is.True,
                "dragging a Scion didn't slow its puller.");
            pulling.TryStopPull(scion, entities.GetComponent<PullableComponent>(scion));
            Assert.That(entities.HasComponent<WFHeavyPullingComponent>(hauler), Is.False);
            // Central's anchor won't come loose, and the one who tries is struck.
            var central = entities.SpawnEntity("WFLatticeCentralAnchor", entities.GetComponent<TransformComponent>(mob).Coordinates);
            var tamper = new UnanchorAttemptEvent(mob, mob);
            entities.EventBus.RaiseLocalEvent(central, tamper);
            Assert.That(tamper.Cancelled, Is.True, "Central's anchor could be unbolted.");
            // Nobody living says the Coda's name cleanly.
            var speech = new Content.Server.Chat.Systems.TransformSpeechEvent(mob, "we're heading into the Coda now");
            entities.EventBus.RaiseEvent(EventSource.Local, speech);
            Assert.That(speech.Message, Does.Not.Contain("Coda"), "someone said the Coda's name cleanly.");
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
        });

        await pair.CleanReturnAsync();
    }
}
