#nullable enable
using System.Linq;
using System.Numerics;
using Content.Client._NF.Guidebook.Controls;
using Content.Client._WG.Cockpit;
using Content.Server._NF.Medical.EntitySystems;
using Content.Server._WG.Companies;
using Content.Server._WG.RoundSetup;
using Content.Server._WG.Waypoints;
using Content.Server.Radio;
using Content.Shared._WG.Waypoints;
using Content.Shared.Examine;
using Content.Shared.Radio;
using Content.Shared.Stealth;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Xenoarchaeology.Artifact.XAE.Components;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WG.Feedback;

/// <summary>Player reports from the second 2026-11-02 feedback export.</summary>
[TestFixture]
public sealed class WGFeedbackBatch1102bTest
{
    /// <summary>Report 412: an artifact gone invisible can still be examined, or a Close inspection node can't trigger.</summary>
    [Test]
    public async Task InvisibleArtifactCanBeExamined()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>("XenoArtifactStealth");
            Assert.That(proto.TryGetComponent<XAEApplyComponentsComponent>(out var apply, entMan.ComponentFactory));

            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var artifact = entMan.SpawnEntity(null, map.GridCoords);
            entMan.AddComponents(artifact, apply!.Components);
            entMan.System<SharedStealthSystem>().SetVisibility(artifact, -1f);

            Assert.That(entMan.System<ExamineSystemShared>().CanExamine(user, artifact));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 410: the assembler's lathe recipes reach the client and show on the guidebook page.</summary>
    [Test]
    public async Task MedicalRecipesPageListsTheAssemblersRecipes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        await server.WaitAssertion(() =>
        {
            var entry = server.System<MedicalRecipeDataSystem>().Registry.Single(it => it.Result == "CombatMedipen");
            Assert.That(entry.Recipes.Single().LatheRecipe?.Id, Is.EqualTo("CombatMedipen"));
        });
        await pair.RunTicksSync(5);

        await client.WaitAssertion(() =>
        {
            var pen = client.ProtoMan.Index<EntityPrototype>("BlankMediPen").Name;
            var page = new GuideMedicalGroupEmbed();
            var sources = WGCockpitLease.Descendants(page).OfType<GuideMedicalSource>().ToList();
            Assert.That(sources, Is.Not.Empty, "The medical recipes page is empty.");

            var made = client.ProtoMan.Index<EntityPrototype>("CombatMedipen").Name;
            var labels = sources
                .Select(source => WGCockpitLease.Descendants(source).OfType<RichTextLabel>().Select(label => label.GetMessage() ?? string.Empty).ToList())
                .Single(texts => texts.Any(text => text.Contains(made)));
            Assert.That(labels.Any(text => text.Contains(pen)), $"The {made} recipe doesn't name its {pen}.");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 402: ghosts hear company and crew radio, the living outside them don't.</summary>
    [Test]
    public async Task GhostsHearPrivateRadioChannels()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var speaker = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var stranger = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var ghost = entMan.SpawnEntity("MobObserver", map.GridCoords);

            foreach (var id in new[] { WGCompanySystem.CompanyChannel, WGRoundSetupSystem.CrewChannel })
            {
                var channel = server.ProtoMan.Index<RadioChannelPrototype>(id);
                var heard = new RadioReceiveAttemptEvent(channel, speaker, ghost);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref heard);
                Assert.That(heard.Cancelled, Is.False, $"A ghost can't hear {id}.");

                var refused = new RadioReceiveAttemptEvent(channel, speaker, stranger);
                entMan.EventBus.RaiseEvent(EventSource.Local, ref refused);
                Assert.That(refused.Cancelled, Is.True, $"An outsider can hear {id}.");
            }
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 401: a piece that splits off a ship keeps its waypoints.</summary>
    [Test]
    public async Task WaypointsSurviveAGridSplit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var grid = map.Grid.Owner;
            EntityUid piece = entMan.System<SharedMapSystem>().CreateGridEntity(map.MapId);
            var console = entMan.SpawnEntity(null, map.GridCoords);
            Assert.That(entMan.System<WGWaypointSystem>().TryAdd(console, "Claim", new Vector2(100f, 200f)));

            var ev = new GridSplitEvent(new[] { piece }, grid);
            entMan.EventBus.RaiseLocalEvent(grid, ref ev, true);

            var waypoints = entMan.GetComponent<WGWaypointsComponent>(piece);
            Assert.That(waypoints.Waypoints.Single().Name, Is.EqualTo("Claim"));
            Assert.That(waypoints.NextId, Is.EqualTo(entMan.GetComponent<WGWaypointsComponent>(grid).NextId));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Report 386: a flare that was already fired once leaves the flare gun when fired again.</summary>
    [Test]
    public async Task FiredFlareLeavesTheFlareGun()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.EntMan;
        var map = await pair.CreateTestMap();
        var flare = EntityUid.Invalid;
        var containers = entMan.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var user = entMan.SpawnEntity("MobHuman", map.GridCoords);
            var gun = entMan.SpawnEntity("WeaponFlareGun", map.GridCoords);
            // A fired flare has lost its casing, so it is ammo that shoots itself.
            flare = entMan.SpawnEntity("FlareActivated", map.GridCoords);
            entMan.RemoveComponent<CartridgeAmmoComponent>(flare);
            Assert.That(containers.Insert(flare, containers.GetContainer(gun, "gun_chamber")));
            var guns = entMan.System<SharedGunSystem>();
            guns.SetBoltClosed(gun, entMan.GetComponent<ChamberMagazineAmmoProviderComponent>(gun), true);

            guns.AttemptShoot(user, gun, entMan.GetComponent<GunComponent>(gun), map.GridCoords.Offset(new Vector2(5f, 0f)));
        });
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() => Assert.That(containers.IsEntityInContainer(flare), Is.False, "The flare stayed in the chamber."));
        await pair.CleanReturnAsync();
    }
}
