#nullable enable
using System.Linq;
using System.Numerics;
using Content.Server.Kitchen.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Server.Power.Components;
using Content.Server._WG.NpcCrew.Components;
using Content.Shared.DoAfter;
using Content.Shared.Emag.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Kitchen.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Player reports from the 2026-11-02 feedback export.</summary>
[TestFixture]
public sealed partial class WGFeedbackBatch1102Test
{
    [Serializable, NetSerializable]
    private sealed partial class DriftDoAfterEvent : DoAfterEvent
    {
        public override DoAfterEvent Clone()
        {
            return this;
        }
    }

    /// <summary>Report 377: drifting doesn't interrupt work on yourself, but steering or work on something else does.</summary>
    [Test]
    public async Task DriftingKeepsWorkOnYourself()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var doAfters = entMan.System<SharedDoAfterSystem>();
        var transform = entMan.System<SharedTransformSystem>();
        var user = EntityUid.Invalid;
        var crate = EntityUid.Invalid;

        await server.WaitPost(() =>
        {
            var space = new MapCoordinates(new Vector2(500f, 500f), map.MapId);
            user = entMan.SpawnEntity("MobHuman", space);
            crate = entMan.SpawnEntity("MobHuman", space);
        });

        bool Working()
        {
            return entMan.GetComponent<DoAfterComponent>(user).DoAfters.Values.Any(doAfter => !doAfter.Cancelled && !doAfter.Completed);
        }

        void Start(EntityUid target)
        {
            var args = new DoAfterArgs(entMan, user, 60f, new DriftDoAfterEvent(), user, target) { BreakOnMove = true };
            Assert.That(doAfters.TryStartDoAfter(args));
        }

        void Drift()
        {
            transform.SetWorldPosition(user, transform.GetWorldPosition(user) + new Vector2(5f, 0f));
        }

        await server.WaitPost(() => Start(user));
        await pair.RunTicksSync(2);
        await server.WaitPost(Drift);
        await pair.RunTicksSync(2);
        Assert.That(Working(), Is.True, "drifting doesn't interrupt work on yourself");

        await server.WaitPost(() =>
        {
            entMan.GetComponent<InputMoverComponent>(user).HeldMoveButtons = MoveButtons.Up;
            Drift();
        });
        await pair.RunTicksSync(2);
        Assert.That(Working(), Is.False, "steering does");

        await server.WaitPost(() =>
        {
            entMan.GetComponent<InputMoverComponent>(user).HeldMoveButtons = MoveButtons.None;
            transform.SetWorldPosition(crate, transform.GetWorldPosition(user));
            Start(crate);
        });
        await pair.RunTicksSync(2);
        await server.WaitPost(Drift);
        await pair.RunTicksSync(2);
        Assert.That(Working(), Is.False, "drifting away from something else still does");

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 378: a powered microwave takes food but leaves an emag in hand.</summary>
    [Test]
    public async Task MicrowaveLeavesEmagInHand()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var hands = entMan.System<SharedHandsSystem>();
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var microwave = entMan.SpawnEntity("KitchenMicrowave", map.GridCoords);
            entMan.GetComponent<ApcPowerReceiverComponent>(microwave).Powered = true;
            var emag = entMan.SpawnEntity("Emag", map.GridCoords);
            var food = entMan.SpawnEntity("FoodApple", map.GridCoords);
            var storage = entMan.GetComponent<MicrowaveComponent>(microwave).Storage;
            Assert.That(entMan.HasComponent<EmagComponent>(emag));

            Assert.That(hands.TryPickupAnyHand(user, emag));
            var coords = entMan.GetComponent<TransformComponent>(microwave).Coordinates;
            var ev = new InteractUsingEvent(user, emag, microwave, coords);
            entMan.EventBus.RaiseLocalEvent(microwave, ev);
            Assert.That(ev.Handled, Is.False);
            Assert.That(storage.Contains(emag), Is.False, "the emag stays out");

            hands.TryDrop(user, emag);
            Assert.That(hands.TryPickupAnyHand(user, food));
            ev = new InteractUsingEvent(user, food, microwave, coords);
            entMan.EventBus.RaiseLocalEvent(microwave, ev);
            Assert.That(ev.Handled, Is.True, "food is taken");
            Assert.That(storage.Contains(food), Is.True, "food still goes in");

            // A microwave that is deleted with something inside trips an engine assert as it empties.
            entMan.DeleteEntity(food);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 379: crew fire semi-automatics at a person's pace, and leave automatic fire and other NPCs alone.</summary>
    [Test]
    public async Task CrewPaceSemiAutomaticFire()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<NPCCombatSystem>();
            var crew = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.AddComponent<WGCrewComponent>(crew);
            var other = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entMan.GetComponent<GunComponent>(entMan.SpawnEntity("WeaponPistolMk58", map.GridCoords));
            var combat = new NPCRangedCombatComponent();

            system.PaceCrewFire(other, gun, combat);
            Assert.That(combat.ShootAccumulator, Is.Zero, "other NPCs aren't paced");

            system.PaceCrewFire(crew, gun, combat);
            Assert.That(combat.ShootDelay - combat.ShootAccumulator, Is.EqualTo(NPCCombatSystem.CrewSemiAutoInterval));

            combat.ShootAccumulator = 0f;
            gun.SelectedMode = SelectiveFire.FullAuto;
            system.PaceCrewFire(crew, gun, combat);
            Assert.That(combat.ShootAccumulator, Is.Zero, "automatic fire keeps its rate");
        });

        await pair.CleanReturnAsync();
    }
}
