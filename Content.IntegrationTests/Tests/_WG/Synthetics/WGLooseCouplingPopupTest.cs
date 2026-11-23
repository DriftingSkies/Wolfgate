#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Server._WG.Synthetics;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Targeting;
using Content.Shared._WG.Wolfmed.Compat;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._WG.Synthetics;

/// <summary>The "passes of cable left" popup on a loose chassis limb answers cable passes only, not passive mending.</summary>
[TestFixture]
public sealed class WGLooseCouplingPopupTest
{
    private const string Tightened = "coupling tightens";

    /// <summary>
    /// Repair nanites close a loose coupling without a word until it seats; a cable pass tells the one holding the coil
    /// how many passes are left.
    /// </summary>
    [Test]
    public async Task NanitesSilentCoilCountsTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var popups = pair.Client.System<WGLooseCouplingPopupProbeSystem>();
        EntityUid body = default, leg = default;
        FixedPoint2 start = default;

        await server.WaitAssertion(() =>
        {
            body = entMan.SpawnEntity("MobIPC", map.GridCoords);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
            leg = entMan.System<SharedBodySystem>()
                .GetBodyChildrenOfType(body, BodyPartType.Leg, symmetry: BodyPartSymmetry.Left).First().Id;

            var limbs = entMan.System<WGSyntheticLimbSystem>();
            Assert.That(entMan.System<WolfmedBodySystem>().TryDetachPart(leg), Is.True);
            Assert.That(limbs.TryFindSocket(body, (leg, entMan.GetComponent<BodyPartComponent>(leg)), out var parent, out var slot));
            Assert.That(limbs.TryReattach(body, leg, parent, slot, body), Is.True);
            start = Loose(entMan, leg) ?? FixedPoint2.Zero;
            Assert.That(start, Is.GreaterThan(FixedPoint2.Zero), "no loose-coupling wound on the leg.");
        });

        await pair.RunTicksSync(10);
        await pair.Client.WaitPost(popups.Start);

        await server.WaitPost(() =>
        {
            var nanites = entMan.EnsureComponent<PlateletFactoriesComponent>(body);
            nanites.DamagePerInterval = new DamageSpecifier { DamageDict = { ["Shock"] = -0.3 } };
            nanites.MendCapabilities = new HashSet<TreatmentCapability> { TreatmentCapability.Mechanical, TreatmentCapability.Electrical };
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            Assert.That(Loose(entMan, leg), Is.LessThan(start), "the nanites did not mend the coupling.");
            entMan.RemoveComponent<PlateletFactoriesComponent>(body);
        });

        await pair.Client.WaitAssertion(() =>
            Assert.That(popups.Received.Count(message => message.Contains(Tightened)), Is.Zero,
                $"passive mending counted as cable passes. Received: {string.Join(" | ", popups.Received)}"));

        await server.WaitAssertion(() =>
        {
            var coil = entMan.SpawnEntity("CableApcStack", map.GridCoords);
            Assert.That(entMan.System<SharedHandsSystem>().TryPickupAnyHand(body, coil), Is.True);
            entMan.GetComponent<TargetingComponent>(body).Target = TargetBodyPart.LeftLeg;
            var interact = new AfterInteractEvent(body, coil, body, map.GridCoords, true);
            entMan.EventBus.RaiseLocalEvent(coil, interact);
            Assert.That(interact.Handled, Is.True, "the coil did not start on the loose leg.");
        });

        await pair.RunTicksSync(90);

        await pair.Client.WaitAssertion(() =>
            Assert.That(popups.Received.Count(message => message.Contains(Tightened)), Is.GreaterThan(0),
                $"a cable pass gave its user no count. Received: {string.Join(" | ", popups.Received)}"));

        await pair.Client.WaitPost(popups.Stop);
        await pair.CleanReturnAsync();
    }

    private static FixedPoint2? Loose(IEntityManager entMan, EntityUid part)
    {
        foreach (var wound in entMan.System<WoundSystem>().GetWounds(part))
        {
            if (wound.Comp.Prototype == WGSyntheticLimbSystem.LooseCouplingWound)
                return wound.Comp.Severity;
        }

        return null;
    }
}

/// <summary>Records the entity popups a connected test client receives, only while a test asks it to.</summary>
public sealed partial class WGLooseCouplingPopupProbeSystem : EntitySystem
{
    [Dependency] private INetManager _net = default!;

    private bool _recording;

    /// <summary>Every popup message received while recording, oldest first.</summary>
    public readonly List<string> Received = new();

    public override void Initialize()
    {
        base.Initialize();

        if (_net.IsClient)
            SubscribeNetworkEvent<PopupEntityEvent>(OnPopup);
    }

    /// <summary>Clears what was recorded and starts recording.</summary>
    public void Start()
    {
        Received.Clear();
        _recording = true;
    }

    /// <summary>Stops recording and drops what was recorded.</summary>
    public void Stop()
    {
        _recording = false;
        Received.Clear();
    }

    private void OnPopup(PopupEntityEvent ev)
    {
        if (_recording)
            Received.Add(ev.Message);
    }
}
