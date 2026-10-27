using System.Collections.Generic;
using System.Linq;
using Content.Client._WF.ExamineCard;
using Content.Server._WF.ExamineCard;
using Content.Shared._WF.ExamineCard;
using Content.Shared.Access.Systems;
using Content.Shared.Inventory;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.ExamineCard;

/// <summary>The card's verdict stamp and worn list, and the client card laying out at its fixed width.</summary>
[TestFixture]
[TestOf(typeof(Server._WF.ExamineCard.ExamineCardSystem))]
public sealed class ExamineCardTest
{
    [Test]
    public async Task StampAndWornTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var cards = entMan.System<Server._WF.ExamineCard.ExamineCardSystem>();
            var inventory = entMan.System<InventorySystem>();
            var idCards = entMan.System<SharedIdCardSystem>();
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var viewer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var name = entMan.GetComponent<MetaDataComponent>(mob).EntityName;

            var card = cards.BuildCard(mob, viewer);
            Assert.Multiple(() =>
            {
                Assert.That(card.Stamp, Is.EqualTo(ExamineCardStamp.NoId));
                Assert.That(card.Name, Is.EqualTo(name));
                Assert.That(card.Species, Is.Not.Null.And.Not.Empty);
                Assert.That(card.Worn, Is.Empty);
            });

            var suit = entMan.SpawnEntity("ClothingUniformJumpsuitColorBlack", map.GridCoords);
            var shoes = entMan.SpawnEntity("ClothingShoesColorBlack", map.GridCoords);
            var id = entMan.SpawnEntity("PassengerIDCard", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, suit, "jumpsuit", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, shoes, "shoes", silent: true, force: true));
            Assert.That(inventory.TryEquip(mob, id, "id", silent: true, force: true));

            Assert.That(idCards.TryChangeFullName(id, $" {name.ToUpperInvariant()} "));
            card = cards.BuildCard(mob, viewer);
            Assert.Multiple(() =>
            {
                Assert.That(card.Stamp, Is.EqualTo(ExamineCardStamp.Match), "case and spacing don't matter");
                Assert.That(card.IdJob, Is.Not.Null.And.Not.Empty);
                Assert.That(card.Worn.Select(w => w.Slot), Is.EqualTo(new[] { "jumpsuit", "id", "shoes" }),
                    "worn items keep the card's slot order");
            });

            Assert.That(idCards.TryChangeFullName(id, "Someone Else"));
            Assert.That(cards.BuildCard(mob, viewer).Stamp, Is.EqualTo(ExamineCardStamp.Mismatch));

            var mask = entMan.SpawnEntity("ClothingMaskGas", map.GridCoords);
            Assert.That(inventory.TryEquip(mob, mask, "mask", silent: true, force: true));
            card = cards.BuildCard(mob, viewer);
            Assert.Multiple(() =>
            {
                Assert.That(card.Stamp, Is.EqualTo(ExamineCardStamp.Unverified));
                Assert.That(card.Name, Is.Null, "a covered face hides the name");
                Assert.That(card.IdName, Is.EqualTo("Someone Else"), "the ID can still be read");
            });

            card = cards.BuildCard(mob, mob);
            Assert.That(card.Name, Is.EqualTo(name), "you always know your own face");

            var validated = Shared._WF.Genitals.Profile.GenitalProfileValidator.ValidateOrgans(
                Shared._WF.Genitals.Profile.GenitalProfile.Empty.WithNsfwText(new string('x', 5000)), server.ProtoMan);
            Assert.That(validated.NsfwText, Has.Length.EqualTo(Shared._WF.Genitals.Profile.GenitalProfile.MaxNsfwTextLength),
                "validation caps the adult description");
            Assert.That(card.Self, Is.True);

            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(entMan.System<Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(mob, wrench));
            card = cards.BuildCard(mob, viewer, detailed: false);
            Assert.Multiple(() =>
            {
                Assert.That(card.Range, Is.EqualTo(ExamineCardRange.Distant));
                Assert.That(card.Stamp, Is.EqualTo(ExamineCardStamp.TooFar));
                Assert.That(card.IdName, Is.Null, "an ID can't be read from afar");
                Assert.That(card.Worn.Select(w => w.Slot), Is.All.StartsWith("hand-").And.Not.Empty,
                    "from afar only held items show");
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The adult description is withheld without consent and sent once both sides opt in and the owner is present.</summary>
    [Test]
    public async Task NsfwConsentTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entMan = server.EntMan;
        EntityUid mob = default;
        EntityUid viewer = default;

        await server.WaitPost(() =>
        {
            mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            viewer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            entMan.EnsureComponent<Server._WF.Genitals.NsfwDescriptionComponent>(mob).Text = "Private.";
        });
        // Identity names are filled in on a later tick.
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var cards = entMan.System<Server._WF.ExamineCard.ExamineCardSystem>();
            var consent = entMan.System<Shared._WF.Genitals.Systems.GenitalConsentSystem>();
            Assert.That(Shared.IdentityManagement.Identity.Name(mob, entMan), Is.EqualTo(entMan.GetComponent<MetaDataComponent>(mob).EntityName),
                "the face shows");
            Assert.That(cards.NsfwTextFor(mob, viewer), Is.Null, "no consent, no adult description");

            var master = Shared._WF.Genitals.Profile.GenitalProfileValidator.GetSettings(server.ProtoMan).MasterConsent;
            foreach (var body in new[] { mob, viewer })
                entMan.EnsureComponent<Content.Shared._Common.Consent.ConsentComponent>(body).ConsentSettings.Toggles[master] = "on";
            Assert.That(consent.ExaminerMayRead(viewer), Is.True, "an adult viewer with adult content on may read");
            Assert.That(cards.NsfwTextFor(mob, viewer), Is.Null, "an owner who isn't present shows nothing");

            entMan.EnsureComponent<Content.Shared.SSDIndicator.SSDIndicatorComponent>(mob).IsSSD = false;
            Assert.That(consent.TargetConsents(mob), Is.True, "the owner consents and is present");
            Assert.That(cards.NsfwTextFor(mob, viewer), Is.EqualTo("Private."), "both opted in and present: sent");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>The real examine and health text of a human folds into chips, held items and nothing else.</summary>
    [Test]
    public async Task FoldsRealExamineTextTest()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var entMan = server.EntMan;
            var cards = entMan.System<Server._WF.ExamineCard.ExamineCardSystem>();
            var examine = entMan.System<Shared.Examine.ExamineSystemShared>();
            var viewer = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var mob = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var wrench = entMan.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(entMan.System<Shared.Hands.EntitySystems.SharedHandsSystem>().TryPickupAnyHand(mob, wrench));

            var card = cards.BuildCard(mob, viewer);
            var chips = new List<ExamineCardChip>();
            var text = ExamineCardText.Fold(examine.GetExamineText(mob, viewer), card.Patterns, chips);
            var health = ExamineCardText.Fold(card.Health!, card.Patterns, chips);

            Assert.Multiple(() =>
            {
                Assert.That(card.Worn.Select(w => w.Slot), Has.Some.StartsWith("hand-"), "held items join the grid");
                Assert.That(text.ToString().Trim(), Is.Empty, "appearance, hands and mind lines all fold away");
                Assert.That(health.ToString().Trim(), Is.Empty, "an unhurt body leaves no health text");
                Assert.That(chips.Select(c => c.Label), Is.SupersetOf(new[]
                {
                    Loc.GetString("wf-examine-chip-catatonic"), Loc.GetString("wf-examine-chip-no-injuries"),
                }));
            });

            entMan.System<Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(mob, Shared.Mobs.MobState.Dead);
            card = cards.BuildCard(mob, viewer);
            chips.Clear();
            ExamineCardText.Fold(examine.GetExamineText(mob, viewer), card.Patterns, chips);
            Assert.That(chips.Select(c => c.Label), Does.Contain(Loc.GetString("wf-examine-chip-departed")));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A real examine request from a connected client brings the card to that client.</summary>
    [Test]
    public async Task ExamineSendsCardTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false });
        var server = pair.Server;
        var client = pair.Client;

        EntityUid player = default;
        EntityUid target = default;
        await server.WaitPost(() =>
        {
            player = pair.Player!.AttachedEntity!.Value;
            target = server.EntMan.SpawnEntity("MobHuman", server.EntMan.GetComponent<TransformComponent>(player).Coordinates);
            server.EntMan.EnsureComponent<Server._WF.Genitals.NsfwDescriptionComponent>(target).Text = "Private.";
        });
        await pair.RunTicksSync(10);

        var net = server.EntMan.GetNetEntity(target);
        await client.WaitAssertion(() =>
        {
            var examine = client.System<Client.Examine.ExamineSystem>();
            examine.DoExamine(client.EntMan.GetEntity(net));
            Assert.That(Descendants(examine.TooltipBox!).OfType<ExamineCardControl>(), Is.Not.Empty,
                "the card shows at once, before the server answers");
            Assert.That(examine.TooltipBox!.GetChild(0).Visible, Is.False, "the plain header with its little sprite is hidden");
            var healthText = Loc.GetString("health-examinable-verb-text");
            Assert.That(Descendants(examine.TooltipBox!).OfType<Client.Examine.ExamineButton>().Where(b => b.Verb.Text == healthText),
                Is.Empty, "the instant card already hides the health button the server's card folds in");
        });
        await pair.RunTicksSync(10);

        var cards = client.System<Client._WF.ExamineCard.ExamineCardSystem>();
        Assert.That(cards.Pending, Is.Null, $"card for {net} still pending: {cards.Pending?.Target}");
        Assert.That(cards.Shown, Is.EqualTo(1), "the tooltip took the card");
        Assert.That(cards.LastShown?.NsfwText, Is.Null, "no adult content consent, so no adult description");

        await pair.CleanReturnAsync();
    }

    /// <summary>Wounds fold into an icon strip on the card, with the full look in a side panel behind a toggle.</summary>
    [Test]
    public async Task ConditionStripTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var health = Robust.Shared.Utility.FormattedMessage.FromMarkupPermissive(
                "[font size=11][color=darkgray]You look it over.[/color][/font][wolfmedlook name=\"torso\" accent=\"fracture\"][/wolfmedlook]" +
                "[wolfmedlookfinding icon=\"blunt\" colour=\"blunt\" label=\"crushed and swollen\" tip=\"crushed and swollen\"][/wolfmedlookfinding]" +
                "[wolfmedlookfinding icon=\"fracture\" colour=\"fracture\" label=\"bent wrong\" tip=\"bent at a wrong angle\"][/wolfmedlookfinding]\n" +
                "[bold]Its torso[/bold]: crushed and swollen, bent wrong.[wolfmedlookend][/wolfmedlookend]");
            var data = new ExamineCardEvent
            {
                Description = string.Concat(Enumerable.Repeat("A tall figure in a long coat, scarred along the jaw. ", 60)),
                NsfwText = string.Concat(Enumerable.Repeat("Private words that run on and on. ", 60)),
                Health = health,
                Patterns = { new ExamineCardPattern(null, ExamineChipTone.Neutral, new[] { "You look it over." }) },
            };

            var box = new Robust.Client.UserInterface.Controls.BoxContainer();
            box.AddChild(new Control());
            var cards = client.System<Client._WF.ExamineCard.ExamineCardSystem>();
            cards.Build(box, EntityUid.Invalid, data, new Robust.Shared.Utility.FormattedMessage());

            var all = Descendants(box).ToList();
            var toggles = all.OfType<ExamineCardToggle>().ToList();
            var descriptionTitle = Loc.GetString("wf-examine-card-description");
            var descriptionToggle = toggles.Single(t => t.Parent!.GetChild(0) is Label { Text: var title } && title == descriptionTitle);
            var toggle = toggles.Single(t => t != descriptionToggle);
            var side = all.OfType<Robust.Client.UserInterface.Controls.BoxContainer>()
                .First(b => b.Orientation == Robust.Client.UserInterface.Controls.BoxContainer.LayoutOrientation.Horizontal && b.ChildCount == 2 && b.GetChild(0) is ExamineCardControl)
                .GetChild(1).GetChild(2);

            Assert.Multiple(() =>
            {
                Assert.That(box.GetChild(0).Visible, Is.False, "the plain header is hidden");
                Assert.That(all.OfType<Robust.Client.UserInterface.Controls.TextureRect>().Count(t => t.SetSize == new System.Numerics.Vector2(16, 16)),
                    Is.EqualTo(2), "one icon per finding");
                Assert.That(side.Visible, Is.False, "the detailed look starts closed");
            });

            Assert.That(toggles, Has.Count.EqualTo(2), "description and condition each have a toggle");
            var preview = all.OfType<Robust.Client.UserInterface.Controls.BoxContainer>().Single(b => b.RectClipContent);
            preview.Measure(new System.Numerics.Vector2(ExamineCardControl.ContentWidth, float.PositiveInfinity));
            Assert.That(preview.DesiredSize.Y, Is.LessThanOrEqualTo(44f), "the card shows only the start of the description");

            descriptionToggle.Toggle();
            var descriptionPanel = side.Parent!.GetChild(0);
            Assert.That(descriptionPanel.Visible, Is.True, "the description toggle opens the whole text beside the card");
            var row = side.Parent!.Parent!;
            row.Measure(new System.Numerics.Vector2(2000, 2000));
            row.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(System.Numerics.Vector2.Zero, row.DesiredSize));
            var cardControl = row.GetChild(0);
            Assert.That(cardControl.Size.Y, Is.EqualTo(cardControl.DesiredSize.Y - cardControl.Margin.SumVertical).Within(1f),
                "a tall side panel doesn't stretch the card's paper down");

            // Both long texts open: together they stay within the card's height, sharing it equally.
            all.OfType<ExamineCardHeartToggle>().Single().Toggle();
            var nsfwPanel = side.Parent!.GetChild(1);
            row.Measure(new System.Numerics.Vector2(2001, 2000));
            row.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(System.Numerics.Vector2.Zero, row.DesiredSize));
            Assert.Multiple(() =>
            {
                Assert.That(nsfwPanel.Visible, Is.True);
                Assert.That(side.Parent!.DesiredSize.Y, Is.LessThanOrEqualTo(cardControl.DesiredSize.Y + 1f),
                    "open side panels never run past the card's bottom");
                Assert.That(descriptionPanel.DesiredSize.Y, Is.EqualTo(nsfwPanel.DesiredSize.Y).Within(2f),
                    "two open texts split the height equally");
            });
            all.OfType<ExamineCardHeartToggle>().Single().Toggle();
            descriptionToggle.Toggle();

            toggle.Toggle();
            Assert.That(side.Visible, Is.True, "the toggle opens the detailed look beside the card");
            Assert.That(Descendants(side).OfType<Robust.Client.UserInterface.Controls.ScrollContainer>(), Is.Not.Empty,
                "the side panel carries the full wound rows");
            toggle.Toggle();
        });

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }

    /// <summary>
    /// A character with everything wrong at once: the card keeps a bounded size, long lists scroll, and every
    /// wound icon stays inside its own row.
    /// </summary>
    [Test]
    public async Task HeavilyWoundedStressTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var data = new ExamineCardEvent { Name = "Patient", Stamp = ExamineCardStamp.NoId };
            foreach (var slot in Server._WF.ExamineCard.ExamineCardSystem.Slots)
                data.Worn.Add(new ExamineCardSlot(slot, NetEntity.Invalid, "a very long item name indeed"));
            data.Worn.Add(new ExamineCardSlot("hand-left", NetEntity.Invalid, "left"));
            data.Worn.Add(new ExamineCardSlot("hand-right", NetEntity.Invalid, "right"));
            var control = new ExamineCardControl(data, EntityUid.Invalid, client.EntMan,
                client.ResolveDependency<IResourceCache>(), client.ResolveDependency<IPrototypeManager>());

            control.SetStatus(Enumerable.Range(0, 30)
                .Select(i => new ExamineCardChip($"CHIP {i}", ExamineChipTone.Danger, "x")).ToList());

            var parts = new[] { "head", "torso", "groin", "left arm", "left hand", "right arm", "right hand", "left leg", "left foot", "right leg", "right foot" };
            var wounds = new List<ExamineCardWound>();
            foreach (var part in parts)
            {
                var wound = new ExamineCardWound(part, Robust.Shared.Maths.Color.Red, part);
                for (var i = 0; i < 9; i++)
                    wound.Findings.Add((Robust.Client.Graphics.Texture.White, Robust.Shared.Maths.Color.Red));
                wounds.Add(wound);
            }
            control.SetCondition(wounds, true, false, _ => { });
            control.SetDescription(string.Concat(Enumerable.Repeat("Long words go on and on. ", 80)), false, _ => { });
            control.SetRemarks(Robust.Shared.Utility.FormattedMessage.FromUnformatted(
                string.Join("\n", Enumerable.Range(0, 40).Select(i => $"Remark line {i}."))));

            control.Measure(new System.Numerics.Vector2(4000, 4000));
            control.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(System.Numerics.Vector2.Zero, control.DesiredSize));

            var all = Descendants(control).ToList();
            Assert.Multiple(() =>
            {
                Assert.That(control.DesiredSize.Y, Is.LessThan(760f), "the card stays a bounded size");
                Assert.That(control.DesiredSize.X, Is.EqualTo(ExamineCardControl.CardWidth + 4).Within(0.5f), "and keeps its width");
                Assert.That(all.OfType<ScrollContainer>().Count(), Is.GreaterThanOrEqualTo(4), "status, condition, worn and remarks scroll");
                Assert.That(all.OfType<ScrollContainer>().All(c => c.Size.Y <= c.MaxHeight + 0.5f), Is.True, "no list grows past its height");

                foreach (var flow in all.OfType<ExamineChipFlow>())
                {
                    foreach (var icon in flow.Children)
                    {
                        Assert.That(icon.Position.X + icon.Size.X, Is.LessThanOrEqualTo(flow.Size.X + 0.5f), "an icon stays inside its row's width");
                        Assert.That(icon.Position.Y + icon.Size.Y, Is.LessThanOrEqualTo(flow.Size.Y + 0.5f), "an icon stays inside its row's height");
                    }
                }

                Assert.That(all.OfType<Label>().Any(l => l.Text == "..."), Is.True, "a cut-off description ends in an ellipsis");
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CardLayoutTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var card = new ExamineCardEvent
            {
                Name = "Averylongname Thatkeepsgoingandgoing Ofthehousehold",
                Appearance = "middle-aged woman",
                Species = "Human",
                IdName = "Someone Else",
                IdJob = "Passenger",
                JobIcon = "JobIconPassenger",
                Stamp = ExamineCardStamp.Mismatch,
                Worn =
                {
                    new ExamineCardSlot("outerClothing", NetEntity.Invalid, "a very long armoured vest name indeed"),
                    new ExamineCardSlot("shoes", NetEntity.Invalid, "black shoes"),
                    new ExamineCardSlot("suitstorage", NetEntity.Invalid, "oxygen tank"),
                    new ExamineCardSlot("jumpsuit", NetEntity.Invalid, "mercenary jumpsuit"),
                    new ExamineCardSlot("back", NetEntity.Invalid, "mercenary backpack"),
                },
            };

            var control = new ExamineCardControl(card, EntityUid.Invalid, client.EntMan,
                client.ResolveDependency<IResourceCache>(), client.ResolveDependency<IPrototypeManager>());
            control.SetStatus(Enumerable.Range(0, 8).Select(i => new ExamineCardChip($"SHORT OF BREATH {i}", ExamineChipTone.Warn, "x")).ToList());
            control.Measure(new System.Numerics.Vector2(900, float.PositiveInfinity));
            control.Arrange(Robust.Shared.Maths.UIBox2.FromDimensions(System.Numerics.Vector2.Zero, control.DesiredSize));
            // Five items is two rows: the worn list shows both whole, nothing clipped at its bottom.
            var wornScroll = Descendants(control).OfType<ScrollContainer>()
                .Single(c => Descendants(c).OfType<Label>().Any(l => l.Text == "mercenary jumpsuit"));
            var wornGrid = wornScroll.GetChild(0);
            Assert.That(wornGrid.Position.Y + wornGrid.Size.Y + wornGrid.Margin.Bottom, Is.LessThanOrEqualTo(wornScroll.Size.Y + 0.5f),
                "the last row of worn items isn't cut off");
            foreach (var worn in new[] { "mercenary jumpsuit", "mercenary backpack" })
            {
                var label = Descendants(control).OfType<Label>().Single(l => l.Text == worn);
                Assert.That(label.DesiredSize.X, Is.LessThanOrEqualTo(label.Size.X + 0.5f), $"'{worn}' fits its tile");
            }
            var chips = Descendants(control).OfType<PanelContainer>().Where(p => p.ToolTip == "x").ToList();
            Assert.That(chips, Has.Count.EqualTo(8));
            Assert.That(chips.Select(c => c.DesiredSize.X).Distinct().Count(), Is.EqualTo(1),
                "status chips stack as one full-width list in the side column");

            Assert.Multiple(() =>
            {
                Assert.That(control.DesiredSize.X, Is.EqualTo(ExamineCardControl.CardWidth + 4).Within(0.5f),
                    "long names wrap instead of widening the card");
                Assert.That(string.Join(" ", Descendants(control).OfType<Label>().Select(l => l.Text)),
                    Does.Contain("Thatkeepsgoingandgoing"), "and are never cut off");
                var bold = Content.Client.Resources.ResourceCacheExtensions.GetFont(client.ResolveDependency<IResourceCache>(), "/Fonts/RobotoMono/RobotoMono-Bold.ttf", 10);
                Assert.That(ExamineCardControl.WrapLines(bold, new string('W', 32), ExamineCardControl.ValueWidth), Has.Count.EqualTo(1),
                    "the longest name a profile allows fits on one line");
                Assert.That(control.DesiredSize.Y, Is.InRange(150f, 480f));
                Assert.That(ExamineCardControl.Serial("Jane Doe"), Is.EqualTo(ExamineCardControl.Serial(" jane doe")));
            });
        });

        await pair.CleanReturnAsync();
    }
}
