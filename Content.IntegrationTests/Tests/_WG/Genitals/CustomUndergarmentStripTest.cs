#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Tests.Interaction;
using Content.Server.GameTicking;
using Content.Shared._Common.Consent;
using Content.Shared._WG.CCVar;
using Content.Shared._WG.CustomMarkings;
using Content.Shared._WG.Genitals;
using Content.Shared._WG.Genitals.Components;
using Content.Shared._WG.Genitals.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Verbs;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using ClientVerbSystem = Content.Client.Verbs.VerbSystem;

namespace Content.IntegrationTests.Tests._WG.Genitals;

/// <summary>
/// Custom markings placed on the underwear layers behave as undergarments: they cover anatomy, the strip verbs detect
/// and remove them, put-back restores them, and the wearer's own toggle hides them just like regular markings.
/// </summary>
[TestOf(typeof(SharedUndergarmentStripSystem))]
public sealed class CustomUndergarmentStripTest : InteractionTest
{
    protected override string PlayerPrototype => "MobHuman";

    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string JumpsuitSlot = "jumpsuit";

    private const string RemoveTop = "wg-undergarment-verb-remove-top";
    private const string RemoveBottom = "wg-undergarment-verb-remove-bottom";
    private const string ReplaceTop = "wg-undergarment-verb-replace-top";
    private const string ReplaceBottom = "wg-undergarment-verb-replace-bottom";
    private const string FailCovered = "wg-undergarment-fail-covered";
    private const string FailCooldown = "wg-undergarment-fail-cooldown";

    private static readonly string[] VerbIds = { RemoveTop, RemoveBottom, ReplaceTop, ReplaceBottom };

    private SharedUndergarmentStripSystem _strip = default!;
    private ILocalizationManager _sLoc = default!;
    private ILocalizationManager _cLoc = default!;
    private float _stripDelay;

    /// <summary>A valid 40-character hex hash used for the custom markings.</summary>
    private static readonly CustomMarking CustomTop = new(new string('a', CustomMarkingRules.HashLength), CustomMarkingPlacement.UndergarmentTop);
    private static readonly CustomMarking CustomBottom = new(new string('b', CustomMarkingRules.HashLength), CustomMarkingPlacement.UndergarmentBottom);

    /// <summary>Gravity and air, so neither drifting nor pressure damage breaks a DoAfter.</summary>
    [SetUp]
    public override async Task Setup()
    {
        await base.Setup();
        await AddGravity();
        await AddAtmosphere();

        _strip = SEntMan.System<SharedUndergarmentStripSystem>();
        _sLoc = Server.ResolveDependency<ILocalizationManager>();
        _cLoc = Client.ResolveDependency<ILocalizationManager>();
        _stripDelay = SEntMan.System<SharedGenitalsSystem>().Settings.StripDelay;
    }

    /// <summary>Custom underwear is detected by coverage, the strip verbs appear, and removal exposes genitals.</summary>
    [Test]
    public async Task CustomUndergarmentCoverageTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);

            // No custom underwear: genitals exposed.
            var coverage = body.GetCoverage();
            Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.None), "No custom undergarments.");

            // Custom bottom only: groin covered.
            body.Humanoid.CustomMarkings.Add(CustomBottom);
            coverage = body.GetCoverage();
            Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.Groin), "Custom bottom covers the groin.");
            var penis = body.Exposure(GenitalSlot.Penis);
            Assert.That(penis.Exposed, Is.False, "Custom bottom hides the penis.");
            Assert.That(penis.CoveredByUndergarment, Is.True, "The custom bottom is the cover.");
            Assert.That(penis.Layer, Is.EqualTo(GenitalLayerSet.Hidden), "The penis layer is Hidden.");

            // Custom top and bottom: both regions covered.
            body.Humanoid.CustomMarkings.Add(CustomTop);
            coverage = body.GetCoverage();
            Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.All), "Both custom undergarments cover.");
            Assert.That(body.Exposure(GenitalSlot.Breasts).Exposed, Is.False, "Custom top hides the breasts.");

            // Removal flags: removing the bottom uncovers the groin.
            body.Genitals.Undergarments = UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;
            coverage = body.GetCoverage();
            Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.Chest), "Removed bottom no longer covers.");
            Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True, "Removed bottom exposes the penis.");

            // Put back: only the Removed bit matters.
            body.Genitals.Undergarments = UndergarmentFlags.BottomByOther;
            Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.All),
                "Only the Removed bit uncovers; ByOther alone still covers.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The wearer hides a custom undergarment via the toggle; it no longer covers anatomy and the strip verbs see nothing.
    /// </summary>
    [Test]
    public async Task HiddenCustomUndergarmentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var body = new TestBody(server.EntMan, map.GridCoords);
            body.Humanoid.CustomMarkings.Add(CustomBottom);

            // Worn: covers.
            Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.Groin),
                "The custom bottom covers while worn.");

            // Hidden via toggle: no longer covers.
            body.Humanoid.HiddenCustomMarkings.Add(CustomBottom);
            Assert.That(body.GetCoverage().Undergarments, Is.EqualTo(GenitalRegion.None),
                "A hidden custom undergarment does not cover.");
            Assert.That(body.Exposure(GenitalSlot.Penis).Exposed, Is.True,
                "Hiding the custom bottom exposes the penis.");

            // The marking is still in CustomMarkings (just hidden).
            Assert.That(body.Humanoid.CustomMarkings, Does.Contain(CustomBottom),
                "A hidden custom marking is still worn.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The player removes and puts back the target's custom bottom through the client's predicted verbs. The flow mirrors
    /// the regular undergarment strip test but uses custom markings instead of prototype markings.
    /// </summary>
    [Test]
    public async Task CustomUndergarmentCompletionTest()
    {
        var target = await SpawnConsentingTargetWithBothCustomUnderwear();
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);
        var removed = UndergarmentFlags.BottomRemoved | UndergarmentFlags.BottomByOther;

        await ClientExecute(RemoveBottom);
        await AwaitStrip();

        var cooldown = TimeSpan.Zero;
        await Server.WaitAssertion(() =>
        {
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(target);
            var coverage = SEntMan.System<GenitalCoverageSystem>().GetCoverage((target, genitals, humanoid));

            Assert.Multiple(() =>
            {
                Assert.That(genitals.Undergarments, Is.EqualTo(removed), "Removal sets Removed and ByOther.");
                Assert.That(genitals.StripCooldownUntil, Is.GreaterThan(STiming.CurTime), "A removal by another player starts the cooldown.");
                Assert.That(_strip.GetRemovalCount(SPlayer, target), Is.EqualTo(1), "The server counts the removal once.");
                Assert.That(coverage.Undergarments, Is.EqualTo(GenitalRegion.Chest), "The removed bottom no longer covers the groin.");
                AssertStates(SPlayer, target, "after the removal", (RemoveTop, FailCooldown), (ReplaceBottom, null));
            });

            cooldown = genitals.StripCooldownUntil;
        });

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(removed),
                "The client must receive the removal."));

        await ClientExecute(ReplaceBottom);
        await AwaitStrip();

        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(genitals.Undergarments, Is.EqualTo(UndergarmentFlags.None),
                    "Put-back clears Removed and ByOther.");
                Assert.That(genitals.StripCooldownUntil, Is.GreaterThan(STiming.CurTime),
                    "A put-back leaves the cooldown.");
            });
        });

        await Client.WaitAssertion(() =>
            Assert.That(CEntMan.GetComponent<GenitalsComponent>(CTarget!.Value).Undergarments, Is.EqualTo(UndergarmentFlags.None),
                "The client must receive the put-back."));
    }

    /// <summary>Clothing over the region disables the strip verb for a custom undergarment, just like regular markings.</summary>
    [Test]
    public async Task CustomUndergarmentCoveredTest()
    {
        var target = await SpawnConsentingTargetWithBothCustomUnderwear();
        var inventory = SEntMan.System<InventorySystem>();

        await Server.WaitAssertion(() =>
        {
            var jumpsuit = SEntMan.SpawnEntity(Jumpsuit, SEntMan.GetCoordinates(TargetCoords));

            Assert.That(inventory.TryEquip(target, jumpsuit, JumpsuitSlot, silent: true, force: true),
                "Could not equip the jumpsuit.");

            AssertStates(SPlayer, target, "jumpsuit on custom underwear", (RemoveTop, FailCovered),
                (RemoveBottom, FailCovered));

            Assert.That(_strip.TryStartStrip(SPlayer, target, UndergarmentSlot.Bottom, true), Is.False,
                "A covered custom undergarment cannot be removed.");

            Assert.That(inventory.TryUnequip(target, JumpsuitSlot, silent: true, force: true),
                "Could not remove the jumpsuit.");
        });
    }

    /// <summary>
    /// Makes the player an adult with adult content on (the actor), and spawns a present adult target one tile away with
    /// adult content, WGUndergarmentStrip, anatomy, and a custom undergarment bottom.
    /// </summary>
    private async Task<EntityUid> SpawnConsentingTargetWithCustomUnderwear()
    {
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            SetAge(SPlayer, 30);
            GenitalTestHelpers.GrantConsent(SEntMan, SPlayer);

            // Exact same setup as UndergarmentStripTest.SpawnConsentingTarget, but with custom instead of regular underwear.
            target = GenitalConsentTestHelpers.SpawnPresentAdult(SEntMan, SEntMan.GetCoordinates(TargetCoords));
            GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
            SEntMan.EnsureComponent<GenitalsComponent>(target);

            // Add custom bottom as the undergarment (replaces SetUndergarments in the original).
            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(target);
            humanoid.CustomMarkings.Add(CustomBottom);
            SEntMan.Dirty(target, humanoid);
        });

        Target = SEntMan.GetNetEntity(target);
        await RunTicks(5);
        return target;
    }

    /// <summary>
    /// Like <see cref="SpawnConsentingTargetWithCustomUnderwear"/> but adds both top and bottom custom undergarments.
    /// </summary>
    private async Task<EntityUid> SpawnConsentingTargetWithBothCustomUnderwear()
    {
        EntityUid target = default;
        await Server.WaitAssertion(() =>
        {
            SetAge(SPlayer, 30);
            GenitalTestHelpers.GrantConsent(SEntMan, SPlayer);

            target = GenitalConsentTestHelpers.SpawnPresentAdult(SEntMan, SEntMan.GetCoordinates(TargetCoords));
            GenitalTestHelpers.GrantConsent(SEntMan, target, GenitalTestHelpers.StripToggle);
            SEntMan.EnsureComponent<GenitalsComponent>(target);

            var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(target);
            humanoid.CustomMarkings.Add(CustomTop);
            humanoid.CustomMarkings.Add(CustomBottom);
            SEntMan.Dirty(target, humanoid);
        });

        Target = SEntMan.GetNetEntity(target);
        await RunTicks(5);
        return target;
    }

    /// <summary>Server thread only.</summary>
    private void SetAge(EntityUid uid, int age)
    {
        var humanoid = SEntMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.Age = age;
        SEntMan.Dirty(uid, humanoid);
    }

    /// <summary>Undergarment verbs the server's handlers build for the user on the target. Server thread only.</summary>
    private List<Verb> ServerVerbs(EntityUid user, EntityUid target)
    {
        return SEntMan.System<SharedVerbSystem>().GetLocalVerbs(target, user, typeof(Verb))
            .Where(v => v.Category == SharedUndergarmentStripSystem.UndergarmentCategory)
            .ToList();
    }

    /// <summary>Undergarment verbs the client's own handlers build for the player on the target. Client thread only.</summary>
    private List<Verb> ClientVerbs()
    {
        return CEntMan.System<SharedVerbSystem>().GetLocalVerbs(CTarget!.Value, CPlayer, typeof(Verb))
            .Where(v => v.Category == SharedUndergarmentStripSystem.UndergarmentCategory)
            .ToList();
    }

    /// <summary>Loc ids of the verbs; a text that matches no id is kept, so a failure shows it.</summary>
    private static List<string> Ids(IEnumerable<Verb> verbs, ILocalizationManager loc)
    {
        return verbs.Select(v => VerbIds.FirstOrDefault(id => loc.GetString(id) == v.Text) ?? v.Text).ToList();
    }

    /// <summary>Asserts exactly these verbs, all enabled. Server thread only.</summary>
    private void AssertVerbs(EntityUid user, EntityUid target, string row, params string[] expected)
    {
        var verbs = ServerVerbs(user, target);
        Assert.That(Ids(verbs, _sLoc), Is.EquivalentTo(expected), row);
        Assert.That(verbs.Where(v => v.Disabled).Select(v => v.Text), Is.Empty, $"{row}: no verb may be disabled.");
    }

    /// <summary>Asserts exactly these verbs; a null reason means enabled, otherwise disabled with that message. Server thread only.</summary>
    private void AssertStates(EntityUid user, EntityUid target, string row, params (string Id, string? Reason)[] expected)
    {
        var verbs = ServerVerbs(user, target);
        Assert.That(Ids(verbs, _sLoc), Is.EquivalentTo(expected.Select(e => e.Id)), row);

        foreach (var (id, reason) in expected)
        {
            var verb = verbs.FirstOrDefault(v => v.Text == _sLoc.GetString(id));
            if (verb == null)
                continue;

            Assert.That(verb.Disabled, Is.EqualTo(reason != null), $"{row}: {id} disabled");
            Assert.That(verb.Message, Is.EqualTo(reason == null ? null : _sLoc.GetString(reason)),
                $"{row}: {id} message");
        }
    }

    /// <summary>Starts a strip by the player on the server, as the verb's action does.</summary>
    private async Task ServerStart(EntityUid target, UndergarmentSlot slot, bool remove)
    {
        await Server.WaitAssertion(() =>
            Assert.That(_strip.TryStartStrip(SPlayer, target, slot, remove), Is.True,
                $"Could not start ({slot}, remove: {remove})."));
    }

    /// <summary>Executes a verb from the client as the context menu does: predicted locally, then run by the server.</summary>
    private async Task ClientExecute(string id)
    {
        await Client.WaitAssertion(() =>
        {
            var verb = ClientVerbs().FirstOrDefault(v => v.Text == _cLoc.GetString(id));
            Assert.That(verb, Is.Not.Null, $"The client offers no {id} verb.");
            Assert.That(verb!.Disabled, Is.False, $"{id} is disabled on the client.");
            CEntMan.System<ClientVerbSystem>().ExecuteVerb(CTarget!.Value, verb!);
        });
    }

    /// <summary>Waits for the server to run the player's strip DoAfter, which must finish uncancelled.</summary>
    private async Task AwaitStrip()
    {
        await RunTicks(5);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1), "The server is not running the strip DoAfter.");
        await AwaitDoAfters();
    }

    /// <summary>
    /// Lets the started DoAfter run for a second, applies the change on the server, and checks it cancelled and changed
    /// nothing. With a reason, the player's client (the actor) must receive exactly that reason once.
    /// </summary>
    private async Task AssertCancelledBy(EntityUid target, string what, string? reason, Action change)
    {
        await RunTicks(3);
        Assert.That(ActiveDoAfters.Count(), Is.EqualTo(1), $"{what}: the strip DoAfter did not start.");
        var doAfter = ActiveDoAfters.Single();
        var genitals = SEntMan.GetComponent<GenitalsComponent>(target);
        var before = genitals.Undergarments;

        await RunSeconds(1);
        await Server.WaitAssertion(change);
        await RunTicks(3);
        Assert.Multiple(() =>
        {
            Assert.That(doAfter.Cancelled, Is.True, $"{what} must cancel the DoAfter.");
            Assert.That(ActiveDoAfters, Is.Empty, $"{what}: no DoAfter may remain.");
        });

        await RunSeconds(_stripDelay);
        Assert.That(genitals.Undergarments, Is.EqualTo(before), $"{what}: the undergarments must not change.");
    }
}

/// <summary>A MobHuman with a penis and breasts mirror, no regular undergarments, and a clean GenitalsComponent.</summary>
/// <remarks>Server thread only.</remarks>
internal sealed class TestBody
{
    public readonly IEntityManager EntMan;
    public readonly GenitalCoverageSystem Coverage;
    public readonly SharedHumanoidAppearanceSystem Humanoids;
    public readonly EntityCoordinates Coords;
    public readonly EntityUid Mob;
    public readonly GenitalsComponent Genitals;
    public readonly HumanoidAppearanceComponent Humanoid;

    public TestBody(IEntityManager entMan, EntityCoordinates coords)
    {
        EntMan = entMan;
        Coverage = entMan.System<GenitalCoverageSystem>();
        Humanoids = entMan.System<SharedHumanoidAppearanceSystem>();
        Coords = coords;

        Mob = entMan.SpawnEntity("MobHuman", coords);
        Humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(Mob);

        Genitals = GenitalTestHelpers.SetMirror(entMan, Mob,
            penis: new GenitalOrganState { Shape = GenitalTestHelpers.PenisHuman, Step = 1, LengthCm = 15 },
            breasts: new GenitalOrganState { Shape = GenitalTestHelpers.BreastsPair, Step = 3 });
        Genitals.RevealMode = GenitalRevealMode.UndergarmentRemoval;
        Genitals.Visibility = default;
        Genitals.Undergarments = UndergarmentFlags.None;
    }

    public Entity<GenitalsComponent> Ent => (Mob, Genitals);

    public GenitalCoverage GetCoverage()
    {
        return Coverage.GetCoverage((Mob, Genitals, Humanoid));
    }

    public GenitalExposure Exposure(GenitalSlot slot)
    {
        return Coverage.GetExposure(Ent, slot, GetCoverage());
    }
}
