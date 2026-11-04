#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Store.Ui;
using Content.Server._WF.CargoLoad;
using Content.Server.Clothing.Systems;
using Content.Shared.Clothing.Components;
using Content.Server.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Store.Systems;
using Content.Shared.Inventory;
using Content.Server._EinsteinEngines.Language;
using Content.Shared.Mind;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Research.Prototypes;
using Content.Shared.Store;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Feedback;

/// <summary>Player reports from the 2026-11-03 feedback export.</summary>
[TestFixture]
public sealed class WFFeedbackBatch1103Test
{
    /// <summary>Reports 447 and 448: typing in a store's search bar filters its listings (uplinks, the contravend).</summary>
    [Test]
    public async Task StoreSearchFiltersListings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            var actor = em.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, actor);
            var store = em.SpawnEntity("VendingMachineSyndieContraband", map.GridCoords);
            em.System<StoreSystem>().ToggleUi(actor, store);
        });
        await pair.RunTicksSync(30);

        int before = 0, after = 0;
        await pair.Client.WaitAssertion(() =>
        {
            var ui = pair.Client.ResolveDependency<IUserInterfaceManager>();
            var menu = Descendants(ui.WindowRoot).OfType<StoreMenu>().Single();
            var listings = menu.FindControl<BoxContainer>("StoreListingsContainer");
            before = listings.ChildCount;

            var search = menu.FindControl<LineEdit>("SearchBar");
            search.GrabKeyboardFocus();
            // The real keyboard path: text goes to whatever control holds keyboard focus.
            var uiInternal = ui.GetType().GetMethod("TextEntered")!;
            foreach (var c in "zzqqxx")
            {
                var args = new Robust.Client.Input.TextEnteredEventArgs(c.ToString());
                uiInternal.Invoke(ui, new object[] { args });
            }

            Assert.That(search.Text, Is.EqualTo("zzqqxx"), "Typed text reaches the search bar.");
            after = listings.ChildCount;
        });

        Assert.That(before, Is.GreaterThan(0), "The contravend shows listings.");
        Assert.That(after, Is.LessThan(before), "A search with no matches hides the listings.");
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 432: the chameleon backpack has back-slot clothing to pick from.</summary>
    [Test]
    public async Task ChameleonBackpackHasTargets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var chameleon = server.EntMan.System<ChameleonClothingSystem>();
            var targets = server.ProtoMan.EnumeratePrototypes<EntityPrototype>()
                .Count(p => chameleon.IsValidTarget(p, SlotFlags.BACK));
            Assert.That(targets, Is.GreaterThan(5), "Back-slot chameleon targets");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 432: every chameleon item starts as a valid pick, or the server never sends its menu any state.</summary>
    [Test]
    public async Task ChameleonDefaultsAreValidTargets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var chameleon = server.EntMan.System<ChameleonClothingSystem>();
            var factory = server.EntMan.ComponentFactory;
            Assert.Multiple(() =>
            {
                foreach (var proto in server.ProtoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || !proto.TryGetComponent<ChameleonClothingComponent>(out var comp, factory) || comp.Default == null)
                        continue;

                    var valid = server.ProtoMan.TryIndex<EntityPrototype>(comp.Default, out var target)
                        && chameleon.IsValidTarget(target, comp.Slot, comp.RequireTag);
                    Assert.That(valid, Is.True, $"{proto.ID} defaults to {comp.Default}, which isn't a valid chameleon target.");
                }
            });
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 431: a dead ghost-role mob doesn't go back on the ghost role list when its player leaves it.</summary>
    [Test]
    public async Task DeadGhostRoleMobIsNotOffered()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var ghostRoles = em.System<GhostRoleSystem>();
            var minds = em.System<SharedMindSystem>();
            var mob = em.SpawnEntity("MobLetoferolHorrorGhostrole", map.GridCoords);
            var role = em.GetComponent<GhostRoleComponent>(mob);

            var mind = minds.CreateMind(null);
            minds.TransferTo(mind, mob);
            Assert.That(Offered(ghostRoles, role), Is.False, "A taken role is off the list.");

            em.System<MobStateSystem>().ChangeMobState(mob, MobState.Dead);
            minds.TransferTo(mind, null);
            Assert.That(Offered(ghostRoles, role), Is.False, "A dead body isn't offered again.");

            em.System<MobStateSystem>().ChangeMobState(mob, MobState.Alive);
            Assert.That(Offered(ghostRoles, role), Is.True, "A revived, empty body is offered again.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 442: a ship's starting stock, a few percent of its hull mass, costs no thrust.</summary>
    [Test]
    public async Task SmallCargoCostsNoThrust()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var profile = server.ProtoMan.Index(WFCargoLoadSystem.DefaultProfile);
            Assert.That(WFCargoLoadSystem.ThrustFactor(profile, 100f, 4f), Is.EqualTo(1f), "4t on a 100t hull is free.");
            Assert.That(WFCargoLoadSystem.ThrustFactor(profile, 100f, 25f), Is.EqualTo(100f / 120f).Within(0.001f),
                "Only the load past the allowance counts.");
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 441: Resomi speak and understand the Avali language.</summary>
    [Test]
    public async Task ResomiKnowScratch()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ProtoMan.Index<EntityPrototype>("MobResomi");
            Assert.That(proto.TryGetComponent<LanguageKnowledgeComponent>(out var known, server.EntMan.ComponentFactory), Is.True);
            Assert.That(known!.SpokenLanguages.Select(l => l.Id), Does.Contain("Scratch"));
            Assert.That(known.UnderstoodLanguages.Select(l => l.Id), Does.Contain("Scratch"));
        });
        await pair.CleanReturnAsync();
    }

    /// <summary>Report 437: the MMC smart gun technology also unlocks the Sakura's magazine.</summary>
    [Test]
    public async Task SakuraTechUnlocksItsMagazine()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var tech = server.ProtoMan.Index<TechnologyPrototype>("MieyoSmartGuns");
            Assert.That(tech.RecipeUnlocks.Select(r => r.Id), Does.Contain("MagazineSmart"));
        });
        await pair.CleanReturnAsync();
    }

    private static bool Offered(GhostRoleSystem ghostRoles, GhostRoleComponent role)
    {
        return ghostRoles.GetGhostRolesInfo(null).Any(info => info.Identifier == role.Identifier);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
