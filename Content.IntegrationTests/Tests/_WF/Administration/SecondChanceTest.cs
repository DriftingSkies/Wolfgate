#nullable enable
using System.Linq;
using Content.Client._WF.Administration.UI.SecondChance;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WF.Administration.Systems;
using Content.Server.Administration.Managers;
using Content.Shared._WF.Administration.SecondChance;
using Content.Shared.Administration;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Ghost;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Administration;

/// <summary>
/// Second chance: the body's player gets a prompt as a ghost, yes puts them back in healed, no leaves them a ghost.
/// </summary>
[TestOf(typeof(SecondChanceSystem))]
public sealed class SecondChanceTest : InteractionTest
{
    private const string Body = "WFSecondChanceTestMob";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WFSecondChanceTestMob
  name: second chance test mob
  components:
  - type: MindContainer
  - type: MobState
  - type: Damageable
    damageContainer: Biological
  - type: MobThresholds
    thresholds:
      0: Alive
      100: Dead
";

    /// <summary>
    /// Offered through the ghost: yes returns the player to the dead body, alive and undamaged.
    /// </summary>
    [Test]
    public async Task AcceptReturnsPlayerHealed()
    {
        var (body, ghost) = await DieAndGhost();
        Assert.That(await Offer(ghost), "Offering through the player's ghost should succeed.");
        await RunTicks(5);

        await ClickControl<SecondChancePromptWindow>("YesButton");
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ServerSession.AttachedEntity, Is.EqualTo(body), "Yes should put the player back in the body.");
                Assert.That(SEntMan.System<MobStateSystem>().IsAlive(body), "The body should be alive again.");
                Assert.That(SEntMan.GetComponent<DamageableComponent>(body).TotalDamage, Is.EqualTo(FixedPoint2.Zero));
                Assert.That(SEntMan.Deleted(ghost), "The ghost should be gone.");
            });
        });
        Assert.That(TryFindWindow<SecondChancePromptWindow>(out _), Is.False, "The prompt should close once answered.");
    }

    /// <summary>
    /// An admin gets the verb on the body; it finds the player who left it. No leaves them a ghost and ends the offer.
    /// </summary>
    [Test]
    public async Task DeclineLeavesGhostAndEndsOffer()
    {
        var admins = Server.ResolveDependency<IAdminManager>();
        await Server.WaitPost(() => admins.PromoteHost(ServerSession));
        for (var i = 0; i < 60 && !admins.HasAdminFlag(ServerSession, AdminFlags.Admin); i++)
        {
            await RunTicks(1);
        }

        Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), "The player never became an admin.");

        var (body, ghost) = await DieAndGhost();
        var text = Server.ResolveDependency<ILocalizationManager>().GetString("wf-admin-verbs-second-chance");

        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>();
            var verb = verbs.GetLocalVerbs(body, ghost, typeof(Verb)).SingleOrDefault(v => v.Text == text);
            Assert.That(verb, Is.Not.Null, "An admin should get the verb on a body.");
            verbs.ExecuteVerb(verb!, ghost, body, forced: true);
        });
        await RunTicks(5);

        var offerId = GetWindow<SecondChancePromptWindow>().OfferId;
        await ClickControl<SecondChancePromptWindow>("NoButton");
        await RunTicks(5);
        Assert.That(TryFindWindow<SecondChancePromptWindow>(out _), Is.False, "The prompt should close once answered.");

        // The offer is spent: a hand-sent yes must do nothing.
        await Client.WaitPost(() =>
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new SecondChanceAnswerEvent(offerId, true)));
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "No should leave the player a ghost.");
                Assert.That(SEntMan.System<MobStateSystem>().IsDead(body), "A declined body should stay dead.");
            });
        });
    }

    /// <summary>
    /// A body no player ever had can't be offered.
    /// </summary>
    [Test]
    public async Task BodyWithoutPlayerIsRefused()
    {
        await SpawnTarget(Body);
        Assert.That(await Offer(STarget!.Value), Is.False);
    }

    /// <summary>
    /// Puts the player in a fresh body, kills it and moves them to a ghost that can't return.
    /// </summary>
    private async Task<(EntityUid Body, EntityUid Ghost)> DieAndGhost()
    {
        EntityUid body = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            var mind = minds.GetOrCreateMind(ServerSession.UserId);
            body = SEntMan.SpawnEntity(Body, SEntMan.GetCoordinates(PlayerCoords));
            minds.TransferTo(mind, body, mind: mind);

            var blunt = ProtoMan.Index<DamageTypePrototype>("Blunt");
            SEntMan.System<DamageableSystem>().TryChangeDamage(body, new DamageSpecifier(blunt, 150), true);
            Assert.That(SEntMan.System<MobStateSystem>().IsDead(body), "The test body should die.");

            minds.TransferTo(mind, null, mind: mind);
        });

        await RunTicks(5);
        var ghost = ServerSession.AttachedEntity!.Value;
        Assert.That(SEntMan.HasComponent<GhostComponent>(ghost), "The player should be a ghost.");
        return (body, ghost);
    }

    private async Task<bool> Offer(EntityUid uid)
    {
        var offered = false;
        await Server.WaitPost(() => offered = SEntMan.System<SecondChanceSystem>().TryOffer(uid, null, out _));
        return offered;
    }
}
