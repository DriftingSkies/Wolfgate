#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.InteractionParticles;
using Content.Shared._WF.InteractionParticles;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using static Robust.UnitTesting.RobustIntegrationTest;

namespace Content.IntegrationTests.Tests._WF.InteractionParticles;

/// <summary>
/// In-hand and inventory particles only ever reach the actor, and the graphics option decides which particles the
/// client draws at all.
/// </summary>
[TestFixture]
[TestOf(typeof(InteractionParticleSystem))]
public sealed class InteractionParticleTest
{
    private const string UseParticle = "WFInteractionParticleUse";
    private const string PullParticle = "WFInteractionParticlePull";

    [Test]
    public async Task InHandParticlesArePrivate()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var map = await pair.CreateTestMap();

        EntityUid actor = default;
        EntityUid held = default;
        EntityUid bystander = default;
        EntityUid bystanderItem = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            actor = entMan.SpawnEntity("MobHuman", map.GridCoords);
            held = entMan.SpawnEntity("DrinkGlass", map.GridCoords);
            bystander = entMan.SpawnEntity("MobHuman", map.GridCoords);
            bystanderItem = entMan.SpawnEntity("DrinkGlass", map.GridCoords);

            // Anything parented to the actor counts as in-hand: the interaction crosses a parent boundary.
            var xform = entMan.System<SharedTransformSystem>();
            xform.SetParent(held, actor);
            xform.SetParent(bystanderItem, bystander);

            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });

        await pair.RunTicksSync(5);

        var interaction = server.System<SharedInteractionSystem>();

        // The default is "except in-hand", so opt in to seeing the actor's own in-hand particles first.
        await client.WaitPost(() => client.CfgMan.SetCVar(InteractionParticleCVars.Mode, (int) InteractionParticleMode.All));

        // The actor's own client receives the private particle.
        var before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, held, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Has.Count.EqualTo(1), "the actor did not see their in-hand particle");
        Assert.That(NewParticles(client, before).Single(), Does.StartWith(UseParticle));

        // Nobody else's in-hand work is sent here.
        await pair.RunTicksSync(20);
        before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(bystander, bystanderItem, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Is.Empty, "another player's in-hand particle reached this client");

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ModesHideParticles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var map = await pair.CreateTestMap();

        EntityUid actor = default;
        EntityUid held = default;
        EntityUid floorItem = default;

        await server.WaitPost(() =>
        {
            var entMan = server.EntMan;
            actor = entMan.SpawnEntity("MobHuman", map.GridCoords);
            held = entMan.SpawnEntity("DrinkGlass", map.GridCoords);
            floorItem = entMan.SpawnEntity("DrinkGlass", map.GridCoords);

            entMan.System<SharedTransformSystem>().SetParent(held, actor);
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });

        await pair.RunTicksSync(5);

        var interaction = server.System<SharedInteractionSystem>();

        // All: the actor sees their in-hand particle.
        await client.WaitPost(() => client.CfgMan.SetCVar(InteractionParticleCVars.Mode, (int) InteractionParticleMode.All));
        var before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, held, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Has.Count.EqualTo(1), "All hid the in-hand particle");

        // Except in-hand: ordinary contacts still show, the actor's own in-hand particle does not.
        await client.WaitPost(() => client.CfgMan.SetCVar(InteractionParticleCVars.Mode, (int) InteractionParticleMode.WithoutInHand));
        await pair.RunTicksSync(20);

        before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, held, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Is.Empty, "Except In-hand still drew an in-hand particle");

        before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, floorItem, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Has.Count.EqualTo(1), "Except In-hand hid an ordinary particle");

        // None: nothing at all.
        await client.WaitPost(() => client.CfgMan.SetCVar(InteractionParticleCVars.Mode, (int) InteractionParticleMode.None));
        await pair.RunTicksSync(20);

        before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, floorItem, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Is.Empty, "None still drew a particle");

        // An unknown value fails closed.
        await client.WaitPost(() => client.CfgMan.SetCVar(InteractionParticleCVars.Mode, 99));
        await pair.RunTicksSync(20);

        before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, floorItem, null, false));
        await pair.RunTicksSync(5);
        Assert.That(NewParticles(client, before), Is.Empty, "an invalid mode drew a particle");

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PullParticleUsesThePullingSprite()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;

        var map = await pair.CreateTestMap();

        EntityUid actor = default;
        EntityUid pullable = default;

        await server.WaitPost(() =>
        {
            actor = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            pullable = server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
        });

        await pair.RunTicksSync(5);

        var interaction = server.System<SharedInteractionSystem>();

        // A predicted pull is drawn by the client that predicted it, so ask for a non-predicted contact to receive it
        // from the server here.
        var before = Particles(client);
        await server.WaitPost(() => interaction.DoContactInteraction(actor, pullable, null, false, interactionParticleType: InteractionParticleType.Pull));
        await pair.RunTicksSync(5);

        var added = NewParticles(client, before);
        Assert.That(added, Has.Count.EqualTo(1), "the pull particle did not spawn");
        Assert.That(added.Single(), Does.StartWith(PullParticle));

        await server.WaitPost(() => server.PlayerMan.SetAttachedEntity(pair.Player!, null));
        await pair.CleanReturnAsync();
    }

    private static HashSet<EntityUid> Particles(ClientIntegrationInstance client)
    {
        var particles = new HashSet<EntityUid>();
        var query = client.EntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            var id = meta.EntityPrototype?.ID;
            if (id == UseParticle || id == PullParticle)
                particles.Add(uid);
        }

        return particles;
    }

    private static List<string> NewParticles(ClientIntegrationInstance client, HashSet<EntityUid> before)
    {
        var added = new List<string>();
        var query = client.EntMan.AllEntityQueryEnumerator<MetaDataComponent>();
        while (query.MoveNext(out var uid, out var meta))
        {
            if (before.Contains(uid))
                continue;

            var id = meta.EntityPrototype?.ID;
            if (id == UseParticle || id == PullParticle)
                added.Add(id!);
        }

        return added;
    }
}
