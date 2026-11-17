using System.Numerics;
using Content.Server.GameTicking;
using Content.Shared._Common.Consent;
using Content.Shared._WG.Genitals;
using Content.Shared._WG.Genitals.Components;
using Content.Shared._WG.Genitals.Profile;
using Content.Shared._WG.Genitals.Prototypes;
using Content.Shared._WG.Genitals.Systems;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.IdentityManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using static Content.IntegrationTests.Tests._WG.Genitals.GenitalTestHelpers;

namespace Content.IntegrationTests.Tests._WG.Genitals;

/// <summary>
/// Anatomy examine lines need both sides' consent, a present target, an adult or bodiless examiner, details range
/// and exposure. Self-examine lists organs others cannot see; loose-organ lines come only from the server.
/// </summary>
[TestFixture]
[TestOf(typeof(GenitalExamineSystem))]
public sealed class GenitalExamineTest
{
    private const string Jumpsuit = "ClothingUniformJumpsuitColorGrey";
    private const string JumpsuitSlot = "jumpsuit";
    private const string PenisOrganProto = "WGOrganPenis";

    private static readonly ProtoId<GenitalShapePrototype> PenisNondescript = "WGGenitalShapePenisNondescript";

    // What another examiner reads about ExamineProfile.
    private const string PenisLine = "penis is exposed: knotted, approximately 21 cm when erect, flaccid.";
    private const string TesticlesLine = "testicles are exposed: large.";
    private const string VulvaLine = "vulva is exposed: slit-shaped.";
    private const string BreastsLine = "breasts are exposed: a pair of breasts, cup D, lactating.";

    private const string OrganLine = "It is a penis: knotted, approximately 21 cm when erect.";

    /// <summary>None of these may appear in an examine text that carries no anatomy line.</summary>
    private static readonly string[] AnatomyWords = { "penis", "testicles", "vulva", "breasts", "womb", "genital" };

    /// <summary>Lines only when both sides have adult content; a missing ConsentComponent means no.</summary>
    [Test]
    public async Task BothSidesConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var examiner = SpawnExaminer(entMan, map.GridCoords);

            var text = Examine(examine, target, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(PenisLine), "Both consent, in range, exposed.");
                Assert.That(text, Does.Contain(TesticlesLine));
                Assert.That(text, Does.Contain(VulvaLine));
                Assert.That(text, Does.Contain(BreastsLine));
                Assert.That(text, Does.Not.Contain("womb"), "The womb is never described.");
            });

            RevokeConsent(entMan, examiner);
            AssertNoAnatomy(Examine(examine, target, examiner), "The examiner has adult content off.");

            entMan.RemoveComponent<ConsentComponent>(examiner);
            Assert.That(entMan.HasComponent<ConsentComponent>(examiner), Is.False, "Precondition: no ConsentComponent.");
            AssertNoAnatomy(Examine(examine, target, examiner), "The examiner has no ConsentComponent.");

            GrantConsent(entMan, examiner);
            Assert.That(Examine(examine, target, examiner), Does.Contain(PenisLine), "The examiner opted in again.");

            RevokeConsent(entMan, target);
            AssertNoAnatomy(Examine(examine, target, examiner), "The target has adult content off.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A disconnected (SSD) target keeps its organs and toggles, but gives no lines until it is present again.</summary>
    [Test]
    public async Task SsdTargetTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var examiner = SpawnExaminer(entMan, map.GridCoords);

            GenitalConsentTestHelpers.SetSsd(entMan, target, true);
            Assert.That(entMan.GetComponent<GenitalsComponent>(target).Penis, Is.Not.Null,
                "Precondition: an SSD body keeps its mirror.");
            AssertNoAnatomy(Examine(examine, target, examiner), "An SSD target.");

            GenitalConsentTestHelpers.SetSsd(entMan, target, false);
            Assert.That(Examine(examine, target, examiner), Does.Contain(PenisLine), "The target is present again.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Minor characters never receive the lines, even with adult content on.</summary>
    [Test]
    public async Task UnderageExaminerTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var examiner = SpawnExaminer(entMan, map.GridCoords);

            SetAge(entMan, examiner, 17);
            AssertNoAnatomy(Examine(examine, target, examiner), "An examiner aged 17.");

            SetAge(entMan, examiner, 18);
            Assert.That(Examine(examine, target, examiner), Does.Contain(PenisLine), "An examiner aged 18.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A living examiner needs details range; a ghost with adult content reads the lines at any distance.</summary>
    [Test]
    public async Task RangeAndGhostTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var farCoords = map.GridCoords.Offset(new Vector2(10f, 0f));

            var far = SpawnExaminer(entMan, farCoords);
            AssertNoAnatomy(Examine(examine, target, far), "A living examiner out of details range.");

            var ghost = entMan.SpawnEntity(GameTicker.ObserverPrototypeName, farCoords);
            AssertNoAnatomy(Examine(examine, target, ghost), "A ghost without adult content.");

            GrantConsent(entMan, ghost);
            var text = Examine(examine, target, ghost);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(PenisLine), "A ghost with adult content, out of details range.");
                Assert.That(text, Does.Contain(BreastsLine));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Covered organs give others no line; show-through-clothing exposes; always-hidden never shows.</summary>
    [Test]
    public async Task ExposureTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var examiner = SpawnExaminer(entMan, map.GridCoords);
            var genitals = entMan.GetComponent<GenitalsComponent>(target);

            Wear(entMan, target, entMan.SpawnEntity(Jumpsuit, map.GridCoords));
            AssertNoAnatomy(Examine(examine, target, examiner), "Everything is under the jumpsuit.");

            genitals.Visibility = genitals.Visibility.With(GenitalSlot.Penis, GenitalVisibility.ShowThroughClothing);
            var text = Examine(examine, target, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(PenisLine), "Show-through-clothing exposes the penis.");
                Assert.That(text, Does.Not.Contain("testicles"), "The other organs stay covered.");
                Assert.That(text, Does.Not.Contain("vulva"));
                Assert.That(text, Does.Not.Contain("breasts"));
            });

            genitals.Visibility = genitals.Visibility.With(GenitalSlot.Penis, GenitalVisibility.AlwaysHidden);
            Wear(entMan, target, null);
            text = Examine(examine, target, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Not.Contain("penis"), "Always-hidden never shows, even unclothed.");
                Assert.That(text, Does.Contain(TesticlesLine), "The other organs are exposed.");
                Assert.That(text, Does.Contain(VulvaLine));
                Assert.That(text, Does.Contain(BreastsLine));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The owner reads exposed organs as "Your", and covered or hidden organs as such. The womb is never described.</summary>
    [Test]
    public async Task SelfExamineTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var target = SpawnTarget(entMan, map.MapCoords);
            var genitals = entMan.GetComponent<GenitalsComponent>(target);

            var text = Examine(examine, target, target);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-penis",
                    ("shape", Loc.GetString("wg-genitals-shape-penis-knotted-examine")),
                    ("length", 21),
                    ("state", Loc.GetString("wg-genitals-state-flaccid")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-testicles",
                    ("size", Loc.GetString("wg-genitals-testicles-size-3")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-vagina",
                    ("shape", Loc.GetString("wg-genitals-shape-vagina-slit-examine")),
                    ("aroused", "no"))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-breasts",
                    ("shape", Loc.GetString("wg-genitals-shape-breasts-pair-examine")),
                    ("cup", Loc.GetString("wg-genitals-cup-letter", ("letter", "D"))),
                    ("lactating", "yes"))));
                Assert.That(text, Does.Not.Contain("womb"));
            });

            Wear(entMan, target, entMan.SpawnEntity(Jumpsuit, map.GridCoords));
            genitals.Visibility = genitals.Visibility.With(GenitalSlot.Vagina, GenitalVisibility.AlwaysHidden);
            text = Examine(examine, target, target);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-covered",
                    ("organ", Loc.GetString("wg-genitals-examine-organ-penis")),
                    ("plural", "no"))), "Self-examine lists covered organs.");
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-covered",
                    ("organ", Loc.GetString("wg-genitals-examine-organ-testicles")),
                    ("plural", "yes"))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-covered",
                    ("organ", Loc.GetString("wg-genitals-examine-organ-breasts")),
                    ("plural", "yes"))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-self-hidden",
                    ("organ", Loc.GetString("wg-genitals-examine-organ-vagina")),
                    ("plural", "no"))));
                Assert.That(text, Does.Not.Contain("exposed"));
                Assert.That(text, Does.Not.Contain("womb"));
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Descriptions: sheath and slit states follow arousal, the vulva clause needs full arousal, the nondescript shape and oversize cups.</summary>
    [Test]
    public async Task DescriptionFollowsAnatomyStateTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var examine = entMan.System<ExamineSystemShared>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var examiner = SpawnExaminer(entMan, map.GridCoords);
            var sheathed = SpawnTarget(entMan, map.MapCoords, FullProfile());
            var slit = SpawnTarget(entMan, map.MapCoords,
                GenitalProfile.Empty.WithPenis(new PenisProfile(PenisHemi, 15, SheathType.Slit)));
            var plain = SpawnTarget(entMan, map.MapCoords, GenitalProfile.Empty
                .WithPenis(new PenisProfile(PenisNondescript, 15))
                .WithBreasts(new BreastsProfile(BreastsPair, 17)));

            var text = Examine(examine, sheathed, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-penis-sheathed",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("sheath", Loc.GetString("wg-genitals-sheath-word-sheath")),
                    ("state", Loc.GetString("wg-genitals-sheath-state-retracted")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-testicles",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("size", Loc.GetString("wg-genitals-testicles-size-2")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-vagina",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("shape", "none"),
                    ("aroused", "no"))), "The human vulva has no shape word.");
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-breasts",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("shape", Loc.GetString("wg-genitals-shape-breasts-pair-examine")),
                    ("cup", Loc.GetString("wg-genitals-cup-letter", ("letter", "C"))),
                    ("lactating", "no"))));
            });

            SetArousal(entMan, sheathed, 50);
            text = Examine(examine, sheathed, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-penis-sheathed",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("sheath", Loc.GetString("wg-genitals-sheath-word-sheath")),
                    ("state", Loc.GetString("wg-genitals-sheath-state-partial")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-vagina",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("shape", "none"),
                    ("aroused", "no"))), "Partial arousal adds no vulva clause (vaginaArousedFrom: Full).");
                Assert.That(text, Does.Not.Contain(Loc.GetString("wg-genitals-examine-vagina",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("shape", "none"),
                    ("aroused", "yes"))));
            });

            SetArousal(entMan, sheathed, 80);
            text = Examine(examine, sheathed, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-penis-sheathed",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("sheath", Loc.GetString("wg-genitals-sheath-word-sheath")),
                    ("state", Loc.GetString("wg-genitals-sheath-state-full")))));
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-vagina",
                    ("target", Identity.Entity(sheathed, entMan)),
                    ("shape", "none"),
                    ("aroused", "yes"))));
            });

            Assert.That(Examine(examine, slit, examiner), Does.Contain(Loc.GetString("wg-genitals-examine-penis-sheathed",
                    ("target", Identity.Entity(slit, entMan)),
                    ("sheath", Loc.GetString("wg-genitals-sheath-word-slit")),
                    ("state", Loc.GetString("wg-genitals-sheath-state-retracted")))));

            SetArousal(entMan, plain, 40);
            text = Examine(examine, plain, examiner);
            Assert.Multiple(() =>
            {
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-penis-noshape",
                    ("target", Identity.Entity(plain, entMan)),
                    ("length", 15),
                    ("state", Loc.GetString("wg-genitals-state-partial")))),
                    "The nondescript shape has no shape word.");
                Assert.That(text, Does.Contain(Loc.GetString("wg-genitals-examine-breasts",
                    ("target", Identity.Entity(plain, entMan)),
                    ("shape", Loc.GetString("wg-genitals-shape-breasts-pair-examine")),
                    ("cup", Loc.GetString("wg-genitals-cup-oversize", ("grade", 2))),
                    ("lactating", "no"))));
            });

            SetArousal(entMan, plain, 70);
            Assert.That(Examine(examine, plain, examiner), Does.Contain(Loc.GetString("wg-genitals-examine-penis-noshape",
                    ("target", Identity.Entity(plain, entMan)),
                    ("length", 15),
                    ("state", Loc.GetString("wg-genitals-state-erect")))));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// A loose organ's line comes only from the server, for an adult examiner with adult content in details range. The
    /// client has no organ data and adds no organ line, but it predicts the body lines.
    /// </summary>
    [Test]
    public async Task LooseOrganServerOnlyTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var client = pair.Client;
        var sEntMan = server.EntMan;
        var cEntMan = client.EntMan;
        var sExamine = sEntMan.System<ExamineSystemShared>();
        var cExamine = cEntMan.System<ExamineSystemShared>();

        var player = await GenitalConsentTestHelpers.AttachToNewBody(pair);
        await GenitalConsentTestHelpers.SetPlayerConsent(pair, MasterToggle);

        EntityUid target = default;
        EntityUid organ = default;
        await server.WaitPost(() =>
        {
            target = SpawnTarget(sEntMan, player.Map.MapCoords.Offset(new Vector2(1f, 0f)));
            organ = sEntMan.SpawnEntity(PenisOrganProto, player.Map.GridCoords.Offset(new Vector2(0.5f, 0f)));
            sEntMan.GetComponent<GenitalOrganComponent>(organ).State = new GenitalOrganState
            {
                Shape = PenisKnotted,
                Step = 2,
                LengthCm = 21,
            };
        });
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            Assert.That(Examine(sExamine, organ, player.Body), Does.Contain(OrganLine), "The server describes a loose organ.");

            var inBody = GenitalOrgan(sEntMan, target, GenitalSlot.Testicles);
            Assert.That(inBody, Is.Not.Null, "Precondition: the target has a testicles organ.");
            Assert.That(Examine(sExamine, inBody.Value, target), Does.Not.Contain("It is a pair of testicles"),
                "An organ inside a body is described only through the body.");

            var optedOut = GenitalConsentTestHelpers.SpawnPresentAdult(sEntMan, player.Map.GridCoords);
            AssertNoAnatomy(Examine(sExamine, organ, optedOut), "An examiner without adult content.");

            var minor = SpawnExaminer(sEntMan, player.Map.GridCoords);
            SetAge(sEntMan, minor, 17);
            AssertNoAnatomy(Examine(sExamine, organ, minor), "An examiner aged 17.");

            var far = SpawnExaminer(sEntMan, player.Map.GridCoords.Offset(new Vector2(10f, 0f)));
            AssertNoAnatomy(Examine(sExamine, organ, far), "An examiner out of details range.");
        });

        var cBody = pair.ToClientUid(player.Body);
        var cTarget = pair.ToClientUid(target);
        var cOrgan = pair.ToClientUid(organ);
        await client.WaitAssertion(() =>
        {
            Assert.That(cEntMan.EntityExists(cOrgan), "Precondition: the client sees the organ.");
            Assert.That(cEntMan.EntityExists(cTarget), "Precondition: the client sees the target.");
            Assert.Multiple(() =>
            {
                AssertNoAnatomy(Examine(cExamine, cOrgan, cBody), "The client adds no loose-organ line.");
                Assert.That(Examine(cExamine, cTarget, cBody), Does.Contain(PenisLine), "The client predicts the body lines.");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A 21 cm knotted penis without a sheath, large external testicles, a slit vulva with a womb, lactating breasts at cup D.</summary>
    private static GenitalProfile ExamineProfile()
    {
        return GenitalProfile.Empty
            .WithPenis(new PenisProfile(PenisKnotted, 21))
            .WithTesticles(new TesticlesProfile(TesticleType.External, 3))
            .WithVagina(new VaginaProfile(VaginaSlit))
            .WithBreasts(new BreastsProfile(BreastsPair, 4, lactation: true));
    }

    /// <summary>An opted-in, present adult MobHuman with built organs (ExamineProfile by default), no undergarments and nothing worn. Server thread only.</summary>
    private static EntityUid SpawnTarget(IEntityManager entMan, MapCoordinates coords, GenitalProfile anatomy = null)
    {
        var mob = SpawnWithAnatomy(entMan, coords, anatomy ?? ExamineProfile());

        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(mob);
        humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentTop);
        humanoid.MarkingSet.RemoveCategory(MarkingCategories.UndergarmentBottom);
        entMan.Dirty(mob, humanoid);

        // Mobs without a player are SSD.
        GenitalConsentTestHelpers.SetSsd(entMan, mob, false);

        Assert.That(entMan.GetComponent<GenitalsComponent>(mob).Penis, Is.Not.Null, "Precondition: the target's organs were built.");
        return mob;
    }

    /// <summary>A present adult MobHuman with adult content on. Server thread only.</summary>
    private static EntityUid SpawnExaminer(IEntityManager entMan, EntityCoordinates coords)
    {
        var mob = GenitalConsentTestHelpers.SpawnPresentAdult(entMan, coords);
        GrantConsent(entMan, mob);
        return mob;
    }

    private static string Examine(ExamineSystemShared examine, EntityUid target, EntityUid examiner)
    {
        return examine.GetExamineText(target, examiner).ToString();
    }

    private static void AssertNoAnatomy(string text, string because)
    {
        foreach (var word in AnatomyWords)
        {
            Assert.That(text, Does.Not.Contain(word), because);
        }
    }

    private static void SetAge(IEntityManager entMan, EntityUid uid, int age)
    {
        var humanoid = entMan.GetComponent<HumanoidAppearanceComponent>(uid);
        humanoid.Age = age;
        entMan.Dirty(uid, humanoid);
    }

    private static void SetArousal(IEntityManager entMan, EntityUid uid, byte arousal)
    {
        entMan.GetComponent<GenitalsComponent>(uid).Arousal = arousal;
    }

    /// <summary>Puts the item in the jumpsuit slot, or empties the slot when the item is null. Server thread only.</summary>
    private static void Wear(IEntityManager entMan, EntityUid mob, EntityUid? item)
    {
        var inventory = entMan.System<InventorySystem>();
        if (inventory.TryGetSlotEntity(mob, JumpsuitSlot, out _))
            Assert.That(inventory.TryUnequip(mob, JumpsuitSlot, silent: true, force: true), "Could not empty the jumpsuit slot.");

        if (item is { } uid)
            Assert.That(inventory.TryEquip(mob, uid, JumpsuitSlot, silent: true, force: true), "Could not equip the jumpsuit.");
    }
}
