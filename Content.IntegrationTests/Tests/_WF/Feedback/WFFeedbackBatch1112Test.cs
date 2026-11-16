#nullable enable
using Content.IntegrationTests.Tests.Interaction;
using Content.Shared._WF.Xenobiology.Components;
using Content.Shared._WF.Xenobiology.Systems;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Stacks;
using System.Collections.Generic;
using System.Linq;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Reports from the 2026-11-12 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1112Test
{
    /// <summary>Report 800: a slime feeds on what its acid hurts and lets go of a medibot instead of feeding forever.</summary>
    [Test]
    public async Task SlimeLetsGoOfWhatItCannotHurt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var latch = entMan.System<SlimeLatchSystem>();

        EntityUid human = default, bot = default, onHuman = default, onBot = default;
        await server.WaitPost(() =>
        {
            human = entMan.SpawnEntity("MobHuman", map.GridCoords);
            bot = entMan.SpawnEntity("MobMedibot", map.GridCoords);
            var xeno = entMan.System<XenobiologySystem>();
            var a = xeno.SpawnSlimeAt(map.GridCoords, "MobSlimeXenobioBaby", "GreyMutation")!.Value;
            var b = xeno.SpawnSlimeAt(map.GridCoords, "MobSlimeXenobioBaby", "GreyMutation")!.Value;
            (onHuman, onBot) = (a.Owner, b.Owner);
            latch.Latch(a, human);
            latch.Latch(b, bot);
        });
        await server.WaitRunTicks(pair.SecondsToTicks(3f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<SlimeComponent>(onHuman).LatchedTarget, Is.EqualTo(human),
                "A slime let go of a body it was feeding on.");
            Assert.That(entMan.GetComponent<DamageableComponent>(human).TotalDamage.Float(), Is.GreaterThan(0f),
                "A latched slime didn't hurt the body it fed on.");
            Assert.That(entMan.GetComponent<SlimeComponent>(onBot).LatchedTarget, Is.Null,
                "A slime kept feeding on a medibot its acid can't hurt.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 665: a wall light with a tube picks up power when cable is run to it after it was built.</summary>
    [Test]
    public async Task LightPowersUpWhenCableArrives()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var maps = entMan.System<SharedMapSystem>();

        EntityUid late = default, early = default;
        await server.WaitPost(() =>
        {
            var plating = server.ResolveDependency<Robust.Shared.Map.ITileDefinitionManager>()["Plating"].TileId;
            for (var y = 0; y <= 12; y++)
                maps.SetTile(map.Grid.Owner, map.Grid.Comp, new Robust.Shared.Maths.Vector2i(0, y), new Robust.Shared.Map.Tile(plating));

            var lights = entMan.System<Content.Shared.Light.EntitySystems.SharedPoweredLightSystem>();
            entMan.SpawnEntity("APCBasic", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            entMan.SpawnEntity("CableApcExtension", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 0.5f));
            early = entMan.SpawnEntity("PoweredlightEmpty", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 12.5f));
            lights.InsertBulb(early, entMan.SpawnEntity("LightTube", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 12.5f)));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(early).Powered, Is.False,
                "The light was powered with no cable near it.");
            for (var y = 1; y <= 12; y++)
                entMan.SpawnEntity("CableApcExtension", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, y + 0.5f));

            // The control: a light built once the cable is there.
            var lights = entMan.System<Content.Shared.Light.EntitySystems.SharedPoweredLightSystem>();
            late = entMan.SpawnEntity("PoweredlightEmpty", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 11.5f));
            lights.InsertBulb(late, entMan.SpawnEntity("LightTube", new Robust.Shared.Map.EntityCoordinates(map.Grid.Owner, 0.5f, 11.5f)));
        });
        await server.WaitRunTicks(pair.SecondsToTicks(2f));

        await server.WaitAssertion(() =>
        {
            Assert.That(entMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(late).Powered, Is.True,
                "A light built next to the cable isn't powered.");
            Assert.That(entMan.GetComponent<Content.Server.Power.Components.ApcPowerReceiverComponent>(early).Powered, Is.True,
                "A light built before the cable was run never picked up power.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 839: a xeno can't open a folding barricade door; a person still can.</summary>
    [Test]
    public async Task XenoCannotOpenBarricadeDoor()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var doors = entMan.System<SharedDoorSystem>();
            var door = entMan.SpawnEntity("CMBarricadeMetalDoor", map.GridCoords);
            if (!entMan.GetComponent<TransformComponent>(door).Anchored)
                entMan.System<SharedTransformSystem>().AnchorEntity(door);
            var xeno = entMan.SpawnEntity("CMXenoDrone", map.GridCoords);
            var person = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var comp = entMan.GetComponent<DoorComponent>(door);

            Assert.That(comp.State, Is.EqualTo(DoorState.Closed));
            Assert.That(doors.CanOpen(door, comp, xeno, quiet: true), Is.False, "A xeno could open a barricade door.");
            Assert.That(doors.CanOpen(door, comp, person, quiet: true), Is.True, "A person couldn't open a barricade door.");
        });
        await pair.CleanReturnAsync();
    }
}

/// <summary>Report 815: a construction step that takes a tagged part uses one from a stack, not the whole stack.</summary>
public sealed class WFFeedbackBatch1112ConstructionTest : InteractionTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: constructionGraph
  id: WFTestStackInsertGraph
  start: start
  graph:
  - node: start
    edges:
    - to: done
      steps:
      - tag: CapacitorStockPart
        name: capacitor
  - node: done

- type: entity
  id: WFTestStackInsert
  components:
  - type: Construction
    graph: WFTestStackInsertGraph
    node: start
";

    /// <summary>Report 845: a wall light can be built.</summary>
    [Test]
    public async Task WallLightCanBeBuilt()
    {
        await StartConstruction("LightTubeFixture");
        await InteractUsing(Steel, 2);
        ClientAssertPrototype("PoweredlightEmpty", Target);
    }

    [Test]
    public async Task TagStepTakesOneFromAStack()
    {
        await SpawnTarget("WFTestStackInsert");
        await InteractUsing("CapacitorStockPart", 3);

        await Server.WaitAssertion(() =>
        {
            Assert.That(Hands.ActiveHandEntity, Is.Not.Null, "The construction step took the whole stack.");
            Assert.That(SEntMan.GetComponent<StackComponent>(Hands.ActiveHandEntity!.Value).Count, Is.EqualTo(2),
                "The construction step didn't take exactly one part.");
        });
    }
}

/// <summary>Report 840: a text link takes the reader to the page of the window it was clicked in.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1112LinkTest
{
    private sealed class LinkHandler : Robust.Client.UserInterface.Controls.BoxContainer,
        Content.Client.Guidebook.RichText.ILinkClickHandler
    {
        public string? Clicked;

        public void HandleClick(string link)
        {
            Clicked = link;
        }
    }

    [Test]
    public async Task LinkGoesToItsOwnWindow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var tags = client.ResolveDependency<Robust.Client.UserInterface.RichText.MarkupTagManager>();
            Assert.That(tags.TryGetMarkupTagHandler("textlink", null, out var handler), Is.True);
            var tag = (Content.Client.Guidebook.RichText.TextLinkTag) handler!;

            var rules = new LinkHandler();
            var other = new LinkHandler();
            Assert.That(tag.TryGetControl(Node("Safe Zones", "RuleSafeZones"), out var rulesLink), Is.True);
            rules.AddChild(rulesLink!);
            // Another window builds a link after the rules page did.
            Assert.That(tag.TryGetControl(Node("Lore", "BasicLore"), out var otherLink), Is.True);
            other.AddChild(otherLink!);

            var click = new Robust.Client.UserInterface.GUIBoundKeyEventArgs(
                Robust.Shared.Input.EngineKeyFunctions.UIClick, Robust.Shared.Input.BoundKeyState.Down,
                new Robust.Shared.Map.ScreenCoordinates(0f, 0f, Robust.Shared.Map.WindowId.Main), true,
                System.Numerics.Vector2.Zero, System.Numerics.Vector2.Zero);
            typeof(Robust.Client.UserInterface.Control)
                .GetMethod("KeyBindDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(rulesLink, new object[] { click });

            Assert.That(rules.Clicked, Is.EqualTo("RuleSafeZones"), "A rules link didn't open its page in the rules window.");
            Assert.That(other.Clicked, Is.Null, "A rules link was handled by another window.");
        });
        await pair.CleanReturnAsync();
    }

    private static MarkupNode Node(string text, string link)
    {
        return new MarkupNode("textlink", new MarkupParameter(text),
            new Dictionary<string, MarkupParameter> { ["link"] = new(link) });
    }
}

/// <summary>Report 793: a sealed Aeolus modsuit helmet draws on a Resomi without a missing sprite.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1112SpriteTest
{
    [TestCase("MobResomi")]
    [TestCase("MobHuman")]
    public async Task SealedHelmetHasNoMissingSprite(string mob)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        EntityUid wearer = default;
        await server.WaitPost(() =>
        {
            wearer = entMan.SpawnEntity(mob, map.GridCoords);
            var helmet = entMan.SpawnEntity("ClothingModsuitHelmetAtmostech", map.GridCoords);
            Assert.That(entMan.System<Content.Shared.Inventory.InventorySystem>().TryEquip(wearer, helmet, "head", force: true), Is.True);
            entMan.System<SharedAppearanceSystem>().SetData(helmet, Content.Shared._WF.Modsuits.Components.ModSuitVisuals.Sealed, true);
        });
        await pair.RunTicksSync(10);

        await pair.Client.WaitAssertion(() =>
        {
            var cEnt = pair.Client.EntMan;
            var uid = cEnt.GetEntity(entMan.GetNetEntity(wearer));
            var sprite = cEnt.GetComponent<Robust.Client.GameObjects.SpriteComponent>(uid);
            var missing = new List<string>();
            var sealedDrawn = false;
            for (var i = 0; i < sprite.AllLayers.Count(); i++)
            {
                if (sprite[i] is not Robust.Client.GameObjects.SpriteComponent.Layer layer || !layer.Visible
                    || layer.ActualRsi is not { } rsi || layer.State.Name is not { } state)
                    continue;
                sealedDrawn |= state.Contains("sealed");
                if (!rsi.TryGetState(state, out _))
                    missing.Add($"{rsi.Path} {state}");
            }
            Assert.That(sealedDrawn, Is.True, $"{mob} didn't draw the sealed helmet.");
            Assert.That(missing, Is.Empty, $"{mob} wearing a sealed Aeolus helmet draws missing sprites.");
        });
        await pair.CleanReturnAsync();
    }
}
