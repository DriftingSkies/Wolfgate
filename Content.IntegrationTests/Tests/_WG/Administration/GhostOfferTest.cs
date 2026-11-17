#nullable enable
using System.Linq;
using Content.Client._WG.Administration.UI.GhostOffer;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server._WG.Administration.Systems;
using Content.Server.Administration.Managers;
using Content.Server.Ghost.Roles.Components;
using Content.Shared._WG.Administration.GhostOffer;
using Content.Shared.Administration;
using Content.Shared.Follower.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Verbs;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;

namespace Content.IntegrationTests.Tests._WG.Administration;

/// <summary>
/// Offer to Ghosts: ghosts get a prompt, signing up enters the draw or takes the role, and only ghosts can sign up.
/// </summary>
[TestOf(typeof(GhostOfferSystem))]
public sealed class GhostOfferTest : InteractionTest
{
    private const string Ghost = "MobObserver";
    private const string Mob = "WGGhostOfferTestMob";
    private const string Role = "WGGhostOfferTestRole";

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: WGGhostOfferTestMob
  name: ghost offer test mob
  components:
  - type: MobState
  - type: MindContainer

- type: entity
  id: WGGhostOfferTestRole
  name: ghost offer test body
  components:
  - type: GhostRole
    name: Test Role
    description: A role that already exists.
  - type: GhostTakeoverAvailable
";

    /// <summary>
    /// A mob nobody plays becomes a raffled role named after it; the ghost who signs up wins the draw and is put in it.
    /// </summary>
    [Test]
    public async Task OfferedMobGoesToGhostWhoSignsUp()
    {
        await BecomeGhost();
        await SpawnTarget(Mob);
        var mob = STarget!.Value;
        Assert.That(await Offer(mob), "Offering a mob nobody plays should succeed.");
        await RunTicks(5);

        var prompt = GetWindow<GhostOfferPromptWindow>();
        var loc = Client.ResolveDependency<ILocalizationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(GetControlFromField<Label>("RoleNameLabel", prompt).Text, Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(mob).EntityName));
            Assert.That(GetControlFromField<Label>("HowLabel", prompt).Text, Is.EqualTo(loc.GetString("wg-ghost-offer-prompt-raffle")));
        });

        await ClickControl<GhostOfferPromptWindow>("SignUpButton");
        await RunTicks(5);
        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.TryGetComponent<GhostRoleRaffleComponent>(mob, out var raffle), "Signing up should start a draw.");
            var entrants = raffle!.CurrentMembers;
            Assert.That(entrants, Does.Contain(ServerSession), "Signing up should enter the draw.");
        });

        // The short draw ends 10 s after the first sign-up.
        await RunSeconds(12);
        Assert.Multiple(() =>
        {
            Assert.That(ServerSession.AttachedEntity, Is.EqualTo(mob), "The only entrant should win the draw.");
            Assert.That(TryFindWindow<GhostOfferPromptWindow>(out _), Is.False, "The prompt should close once the role is taken.");
        });
        Assert.That(await Offer(mob), Is.False, "A taken mob can't be offered again.");
    }

    /// <summary>
    /// An admin gets the Offer to Ghosts verb on a mob nobody plays, and using it offers the mob.
    /// </summary>
    [Test]
    public async Task AdminVerbOffersMob()
    {
        var admins = Server.ResolveDependency<IAdminManager>();
        await Server.WaitPost(() => admins.PromoteHost(ServerSession));
        for (var i = 0; i < 60 && !admins.HasAdminFlag(ServerSession, AdminFlags.Admin); i++)
        {
            await RunTicks(1);
        }

        Assert.That(admins.HasAdminFlag(ServerSession, AdminFlags.Admin), "The player never became an admin.");

        var ghost = await BecomeGhost();
        await SpawnTarget(Mob);
        var mob = STarget!.Value;
        var text = Server.ResolveDependency<ILocalizationManager>().GetString("wg-admin-verbs-offer-to-ghosts");

        await Server.WaitAssertion(() =>
        {
            var verbs = SEntMan.System<SharedVerbSystem>();
            var verb = verbs.GetLocalVerbs(mob, ghost, typeof(Verb)).SingleOrDefault(v => v.Text == text);
            Assert.That(verb, Is.Not.Null, "An admin should get the verb on a mob nobody plays.");
            verbs.ExecuteVerb(verb!, ghost, mob, forced: true);
        });
        await RunTicks(5);

        var prompt = GetWindow<GhostOfferPromptWindow>();
        Assert.That(GetControlFromField<Label>("RoleNameLabel", prompt).Text,
            Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(mob).EntityName), "The verb should offer the mob to ghosts.");
    }

    /// <summary>
    /// An open ghost role keeps its own text and first-come rule, and signing up takes it at once.
    /// </summary>
    [Test]
    public async Task OpenGhostRoleIsAdvertisedAsItIs()
    {
        await BecomeGhost();
        await SpawnTarget(Role);
        var role = STarget!.Value;
        Assert.That(await Offer(role), "Offering an open ghost role should succeed.");
        await RunTicks(5);

        var prompt = GetWindow<GhostOfferPromptWindow>();
        var loc = Client.ResolveDependency<ILocalizationManager>();
        Assert.Multiple(() =>
        {
            Assert.That(GetControlFromField<Label>("RoleNameLabel", prompt).Text, Is.EqualTo("Test Role"));
            Assert.That(GetControlFromField<Label>("HowLabel", prompt).Text, Is.EqualTo(loc.GetString("wg-ghost-offer-prompt-first-come")));
        });

        await ClickControl<GhostOfferPromptWindow>("SignUpButton");
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(ServerSession.AttachedEntity, Is.EqualTo(role), "A first-come role should be taken at once.");
            Assert.That(TryFindWindow<GhostOfferPromptWindow>(out _), Is.False, "The prompt should close once the role is taken.");
        });
    }

    /// <summary>
    /// A player's own body can't be offered, and sign-ups and follows from a non-ghost are ignored.
    /// </summary>
    [Test]
    public async Task OnlyGhostsCanSignUpOrFollow()
    {
        Assert.That(await Offer(SPlayer), Is.False, "A mob with a player can't be offered.");

        var body = SPlayer;
        await BecomeGhost();
        await SpawnTarget(Mob);
        var mob = STarget!.Value;
        Assert.That(await Offer(mob), "Offering a mob nobody plays should succeed.");
        await RunTicks(5);
        var offerId = GetWindow<GhostOfferPromptWindow>().OfferId;

        // Back in a living body: the prompt closes, and a hand-sent sign-up or follow must do nothing.
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.TransferTo(ServerSession.ContentData()!.Mind!.Value, body);
        });
        await RunTicks(5);
        Assert.Multiple(() =>
        {
            Assert.That(ServerSession.AttachedEntity, Is.EqualTo(body), "The player should be back in the body.");
            Assert.That(TryFindWindow<GhostOfferPromptWindow>(out _), Is.False, "Leaving the ghost should close the prompt.");
        });

        await Client.WaitPost(() =>
        {
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOfferSignUpEvent(offerId));
            CEntMan.EntityNetManager!.SendSystemNetworkMessage(new GhostOfferFollowEvent(offerId));
        });
        await RunTicks(5);

        await Server.WaitAssertion(() =>
        {
            Assert.That(SEntMan.HasComponent<GhostRoleRaffleComponent>(mob), Is.False, "A non-ghost sign-up shouldn't start a draw.");
            Assert.That(SEntMan.HasComponent<FollowerComponent>(body), Is.False, "A non-ghost shouldn't follow the offered mob.");
        });
    }

    /// <summary>
    /// Moves the player, with a new mind, into a fresh ghost.
    /// </summary>
    private async Task<EntityUid> BecomeGhost()
    {
        EntityUid ghost = default;
        await Server.WaitPost(() =>
        {
            var minds = SEntMan.System<SharedMindSystem>();
            minds.WipeMind(ServerSession.ContentData()?.Mind);
            ghost = SEntMan.SpawnEntity(Ghost, SEntMan.GetCoordinates(PlayerCoords));
            minds.TransferTo(minds.CreateMind(ServerSession.UserId).Owner, ghost);
        });

        await RunTicks(5);
        Assert.That(ServerSession.AttachedEntity, Is.EqualTo(ghost), "The player should be the ghost.");
        return ghost;
    }

    private async Task<bool> Offer(EntityUid uid)
    {
        var offered = false;
        await Server.WaitPost(() => offered = SEntMan.System<GhostOfferSystem>().TryOffer(uid, null, null, null, null, out _));
        return offered;
    }
}
