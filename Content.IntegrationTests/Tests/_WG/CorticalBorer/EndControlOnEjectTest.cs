#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._Mono.CorticalBorer;
using Content.Server._WG.CorticalBorer;
using Content.Shared._Mono.CorticalBorer;
using Content.Shared._Shitmed.Medical.Surgery;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mind;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.IntegrationTests.Tests._WG.CorticalBorer;

/// <summary>
/// A cortical borer taken out of a host it is controlling gives the body back: the host's player and the borer's mind
/// return to their own bodies and the stand-in that held the host's mind is gone. Taking out a borer that isn't
/// controlling stays clean.
/// </summary>
[TestFixture]
[TestOf(typeof(EndControlOnEjectSystem))]
public sealed class EndControlOnEjectTest
{
    private const string HostProto = "MobHuman";
    private const string BorerProto = "MobCorticalBorer";
    private const string Surgery = "SurgeryCorticalBorerRemoval";
    private const string RemovalStep = "SurgeryStepRemoveCorticalBorer";

    /// <summary>How a controlling borer comes out of its host.</summary>
    public enum Exit
    {
        /// <summary>The removal surgery, which ejects without ending control.</summary>
        Surgery,

        /// <summary>Mono's eject on its own.</summary>
        Eject,

        /// <summary>What the lost head and polymorph paths do: end control, then eject.</summary>
        EndControlThenEject,
    }

    [TestCase(Exit.Surgery)]
    [TestCase(Exit.Eject)]
    [TestCase(Exit.EndControlThenEject)]
    public async Task EjectDuringControlReturnsMinds(Exit exit)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var player = pair.Player!;

        var entMan = server.EntMan;
        var borerSys = entMan.System<CorticalBorerSystem>();

        var host = EntityUid.Invalid;
        var borer = EntityUid.Invalid;
        var hostMind = EntityUid.Invalid;
        var borerMind = EntityUid.Invalid;
        var standIn = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            (host, borer, hostMind, borerMind) = Infest(entMan, map.GridCoords, player);
        });

        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            var borerComp = entMan.GetComponent<CorticalBorerComponent>(borer);
            borerSys.TakeControlHost((borer, borerComp), entMan.GetComponent<CorticalBorerInfestedComponent>(host));

            Assert.That(borerComp.ControlingHost, Is.True, "The borer didn't take control.");
            Assert.That(Body(entMan, borerMind), Is.EqualTo(host), "Control didn't put the borer's mind in the host.");
            Assert.That(Body(entMan, hostMind), Is.Not.Null.And.Not.EqualTo(host),
                "Control didn't move the host's mind into a stand-in.");
            standIn = Body(entMan, hostMind)!.Value;
            Assert.That(player.AttachedEntity, Is.EqualTo(standIn), "Control didn't move the host's player.");
        });

        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            Entity<CorticalBorerComponent> borerEnt = (borer, entMan.GetComponent<CorticalBorerComponent>(borer));
            switch (exit)
            {
                case Exit.Surgery:
                    RemoveBySurgery(entMan, host, map.GridCoords);
                    break;
                case Exit.Eject:
                    Assert.That(borerSys.TryEjectBorer(borerEnt), Is.True, "The borer wasn't ejected.");
                    break;
                case Exit.EndControlThenEject:
                    borerSys.EndControl(borerEnt);
                    Assert.That(borerSys.TryEjectBorer(borerEnt), Is.True, "The borer wasn't ejected.");
                    break;
            }
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            AssertEjected(entMan, player, host, borer, hostMind, borerMind);
            Assert.That(entMan.Deleted(standIn), Is.True, "The stand-in that held the host's mind is still around.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovalWithoutControlStaysClean()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var player = pair.Player!;

        var entMan = server.EntMan;

        var host = EntityUid.Invalid;
        var borer = EntityUid.Invalid;
        var hostMind = EntityUid.Invalid;
        var borerMind = EntityUid.Invalid;

        await server.WaitAssertion(() =>
        {
            (host, borer, hostMind, borerMind) = Infest(entMan, map.GridCoords, player);
        });

        await pair.RunSeconds(1);

        await server.WaitAssertion(() =>
        {
            RemoveBySurgery(entMan, host, map.GridCoords);
        });

        await pair.RunSeconds(2);

        await server.WaitAssertion(() =>
        {
            AssertEjected(entMan, player, host, borer, hostMind, borerMind);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// Spawns a host the player owns and a borer with a mind of its own, and puts the borer in the host.
    /// </summary>
    private static (EntityUid Host, EntityUid Borer, EntityUid HostMind, EntityUid BorerMind) Infest(
        IEntityManager entMan,
        EntityCoordinates coords,
        ICommonSession player)
    {
        var mindSys = entMan.System<SharedMindSystem>();

        var host = entMan.SpawnEntity(HostProto, coords);
        var hostMind = mindSys.CreateMind(player.UserId).Owner;
        mindSys.TransferTo(hostMind, host);
        Assert.That(player.AttachedEntity, Is.EqualTo(host), "The player didn't get the host's body.");

        var borer = entMan.SpawnEntity(BorerProto, coords);
        var borerMind = mindSys.CreateMind(null).Owner;
        mindSys.TransferTo(borerMind, borer);

        var borerComp = entMan.GetComponent<CorticalBorerComponent>(borer);
        entMan.System<CorticalBorerSystem>().InfestTarget((borer, borerComp), host);
        Assert.That(borerComp.Host, Is.EqualTo(host), $"{BorerProto} couldn't infest {HostProto}.");

        return (host, borer, hostMind, borerMind);
    }

    /// <summary>
    /// Runs the removal step as OnTargetDoAfter does once the surgery is valid: SurgeryStepEvent on the step singleton.
    /// </summary>
    private static void RemoveBySurgery(IEntityManager entMan, EntityUid host, EntityCoordinates coords)
    {
        var surgerySys = entMan.System<SharedSurgerySystem>();
        var surgery = surgerySys.GetSingleton(Surgery);
        var step = surgerySys.GetSingleton(RemovalStep);
        Assert.That(surgery, Is.Not.Null, $"{Surgery} has no singleton.");
        Assert.That(step, Is.Not.Null, $"{RemovalStep} has no singleton.");

        var surgeon = entMan.SpawnEntity(HostProto, coords);
        var head = entMan.System<SharedBodySystem>().GetBodyChildrenOfType(host, BodyPartType.Head).First().Id;

        var ev = new SurgeryStepEvent(surgeon, host, head, new List<EntityUid>(), surgery!.Value);
        entMan.EventBus.RaiseLocalEvent(step!.Value, ref ev);
    }

    /// <summary>
    /// Checks the borer is out of the host and controls nothing, and the player and each mind are in their own bodies.
    /// </summary>
    private static void AssertEjected(
        IEntityManager entMan,
        ICommonSession player,
        EntityUid host,
        EntityUid borer,
        EntityUid hostMind,
        EntityUid borerMind)
    {
        var borerComp = entMan.GetComponent<CorticalBorerComponent>(borer);

        Assert.Multiple(() =>
        {
            Assert.That(borerComp.Host, Is.Null, "The borer still has a host.");
            Assert.That(entMan.System<SharedContainerSystem>().IsEntityInContainer(borer), Is.False,
                "The borer is still inside something.");
            Assert.That(entMan.HasComponent<CorticalBorerInfestedComponent>(host), Is.False,
                "The host still counts as infested.");
            Assert.That(borerComp.ControlingHost, Is.False, "The borer still counts as controlling a host.");
            Assert.That(Body(entMan, hostMind), Is.EqualTo(host), "The host's mind isn't in the host's body.");
            Assert.That(Body(entMan, borerMind), Is.EqualTo(borer), "The borer's mind isn't in the borer.");
            Assert.That(player.AttachedEntity, Is.EqualTo(host), "The host's player isn't in the host's body.");
        });
    }

    private static EntityUid? Body(IEntityManager entMan, EntityUid mind)
    {
        return entMan.GetComponent<MindComponent>(mind).OwnedEntity;
    }
}
