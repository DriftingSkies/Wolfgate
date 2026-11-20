#nullable enable
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Ghost;
using Content.Server._WG.Ordnance;
using Content.Server._WG.Warheads;
using Content.Shared._WG.Ghost;
using Content.Shared._WG.Warheads;
using Content.Shared._WG.Xenonids.Parasite;
using Content.Shared.Follower.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using ClientNotices = Content.Client._WG.Ghost.GhostNoticeSystem;

namespace Content.IntegrationTests.Tests._WG.Ghost;

/// <summary>
/// Ghost notices reach ghosts as chips that orbit the source when clicked, and the game loop's live events (Scions,
/// warheads, planted drop beacons) are listed in the orbit menu.
/// </summary>
[TestOf(typeof(GhostNoticeSystem))]
public sealed class GhostNoticeTest : InteractionTest
{
    [Test]
    public async Task AChipOrbitsWhatHappened()
    {
        var ghost = await BecomeGhost();
        var notices = SEntMan.System<GhostNoticeSystem>();
        var chips = CEntMan.System<ClientNotices>();
        EntityUid victim = default;

        await Server.WaitPost(() =>
        {
            victim = SEntMan.SpawnEntity("MobHuman", SEntMan.GetCoordinates(TargetCoords));
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new XenoParasiteInfectEvent(victim, victim));
            // The same notice about the same victim again is dropped.
            SEntMan.EventBus.RaiseEvent(EventSource.Local, new XenoParasiteInfectEvent(victim, victim));
        });
        await RunTicks(10);

        await Server.WaitAssertion(() =>
            Assert.That(notices.Recent.Count(n => n.Source == SEntMan.GetNetEntity(victim)), Is.EqualTo(1),
                "An infection sends one notice, however often it is raised."));

        await Client.WaitAssertion(() =>
        {
            Assert.That(chips.Chips, Has.Count.EqualTo(1), "The ghost got no chip.");
            Assert.That(chips.Chips[0].IsInsideTree, Is.True, "The chip is not on screen.");
        });

        await ClickControl(chips.Chips[0]);
        await RunTicks(10);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<FollowerComponent>(ghost, out var follower), Is.True, "The click did not orbit.");
            Assert.That(follower!.Following, Is.EqualTo(victim));
        });
        await Client.WaitAssertion(() => Assert.That(chips.Chips, Is.Empty, "The chip stayed after the click."));
    }

    [Test]
    public async Task WarheadsAndBeaconsAreListedAndAnnounced()
    {
        await BecomeGhost();
        var orbit = SEntMan.System<GhostOrbitSystem>();
        var notices = SEntMan.System<GhostNoticeSystem>();

        await Server.WaitAssertion(() =>
        {
            var coords = SEntMan.GetCoordinates(TargetCoords);
            var warhead = SEntMan.SpawnEntity("WGWarheadTacticalKeyed", coords);
            var beacon = SEntMan.SpawnEntity("WGOrdnanceBeaconTactical", coords);

            GhostOrbitTarget? Find(EntityUid uid) => orbit.BuildTargets(EntityUid.Invalid, isAdmin: false)
                .Where(t => t.Entity == SEntMan.GetNetEntity(uid))
                .Select(t => (GhostOrbitTarget?) t)
                .FirstOrDefault();

            var listed = Find(warhead);
            Assert.That(listed, Is.Not.Null, "A warhead is not listed.");
            Assert.That(listed!.Value.Category, Is.EqualTo(GhostOrbitCategory.Interest));
            Assert.That(listed.Value.Detail, Is.EqualTo(Server.ResolveDependency<ILocalizationManager>().GetString("wg-ghost-orbit-interest-warhead-safe")));
            Assert.That(Find(beacon), Is.Null, "A beacon nobody planted is listed.");

            SEntMan.System<WGOrdnanceSystem>().Plant((beacon, SEntMan.GetComponent<WGOrdnanceBeaconComponent>(beacon)), null);
            Assert.That(Find(beacon), Is.Not.Null, "A planted beacon is not listed.");

            var warheads = SEntMan.System<WGWarheadSystem>();
            var comp = SEntMan.GetComponent<WGWarheadComponent>(warhead);
            warheads.StartCountdown((warhead, comp), null);
            Assert.That(Find(warhead)!.Value.Detail, Is.EqualTo(Server.ResolveDependency<ILocalizationManager>().GetString("wg-ghost-orbit-interest-warhead-countdown")));
            Assert.That(notices.Recent.Any(n => n.Source == SEntMan.GetNetEntity(warhead)), Is.True, "A countdown sent no notice.");
            Assert.That(notices.Recent.Any(n => n.Source == SEntMan.GetNetEntity(beacon)), Is.True, "Planting a beacon sent no notice.");

            warheads.Disarm((warhead, comp), null, force: true);
            SEntMan.DeleteEntity(warhead);
            SEntMan.DeleteEntity(beacon);
        });
    }

    /// <summary>Moves the player, with a new mind, into a fresh ghost.</summary>
    private async Task<EntityUid> BecomeGhost()
    {
        EntityUid ghost = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity("MobObserver", SEntMan.GetCoordinates(PlayerCoords));
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player should be the ghost.");
        return ghost;
    }
}
