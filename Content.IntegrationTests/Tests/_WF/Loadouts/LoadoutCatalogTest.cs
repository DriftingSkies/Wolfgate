#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Client._WF.Loadouts;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._WF.Loadouts;

/// <summary>The loadout window's grouping and listing hold for every role, and the window opens for each of them.</summary>
[TestFixture]
[TestOf(typeof(LoadoutCategorizer))]
[TestOf(typeof(LoadoutCatalog))]
[TestOf(typeof(WolfgateLoadoutWindow))]
public sealed class LoadoutCatalogTest
{
    private const string Role = "JobContractor";

    /// <summary>Groups whose ids say they hold weapons or ammunition.</summary>
    private static readonly Regex WeaponId = new("Firearm|Pistol|Sidearm|Ammo|Gun|Primary|ArmorPlate|Weapon|Mag$");

    private static readonly (string Group, LoadoutCategory Category)[] Pinned =
    {
        ("MercenaryJumpsuit", LoadoutCategory.Clothing),
        ("MercenaryOuterClothing", LoadoutCategory.Clothing),
        ("MercenaryHead", LoadoutCategory.HeadFace),
        ("MercenaryFace", LoadoutCategory.HeadFace),
        ("MercenaryBalaclava", LoadoutCategory.HeadFace),
        ("MercenaryBackpack", LoadoutCategory.Bags),
        ("ContractorWallet", LoadoutCategory.Bags),
        ("ContractorFirearm", LoadoutCategory.Weapons),
        ("ContractorMag", LoadoutCategory.Weapons),
        ("CivGunCaseSpecial", LoadoutCategory.Weapons),
        ("ContractorUtility", LoadoutCategory.Tools),
        ("ContractorImplanter", LoadoutCategory.Tools),
        ("ContractorTrinkets", LoadoutCategory.Extras),
    };

    [Test]
    public async Task GroupsLandInSensibleCategories()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();

        await client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var (id, category) in Pinned)
                {
                    Assert.That(LoadoutCategorizer.Categorize(proto.Index<LoadoutGroupPrototype>(id), proto), Is.EqualTo(category), id);
                }

                // A name key nothing uses any more means an upstream rename left its groups uncategorised.
                var names = proto.EnumeratePrototypes<LoadoutGroupPrototype>().Select(g => g.Name.Id).ToHashSet();
                foreach (var key in LoadoutCategorizer.NameCategories.Keys)
                {
                    Assert.That(names, Does.Contain(key), $"no loadout group is named {key}");
                }

                foreach (var role in proto.EnumeratePrototypes<RoleLoadoutPrototype>())
                {
                    var categories = role.Groups
                        .Where(g => proto.TryIndex(g, out var group) && !group.Hidden)
                        .ToDictionary(g => g.Id, g => LoadoutCategorizer.Categorize(proto.Index(g), proto));

                    foreach (var (group, category) in categories)
                    {
                        if (WeaponId.IsMatch(group))
                            Assert.That(category, Is.EqualTo(LoadoutCategory.Weapons), $"{role.ID}: {group}");
                    }

                    // Extras is the catch-all; a role piling up there has groups the rules no longer recognise.
                    var extras = categories.Where(c => c.Value == LoadoutCategory.Extras).Select(c => c.Key).ToList();
                    Assert.That(extras, Has.Count.LessThanOrEqualTo(6), $"{role.ID} extras: {string.Join(", ", extras)}");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The catalog lists what the stock window listed: every loadout of a group and of its subgroups, one level
    /// deep, that isn't hidden from the character. A group is listed exactly when that leaves something.
    /// </summary>
    [Test]
    public async Task CatalogListsWhatTheGroupsOffer()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");

            Assert.Multiple(() =>
            {
                foreach (var role in proto.EnumeratePrototypes<RoleLoadoutPrototype>())
                {
                    var loadout = new RoleLoadout(role.ID);
                    loadout.SetDefault(profile, session, proto);
                    var catalog = new LoadoutCatalog(profile, loadout, role, session, collection);
                    catalog.Sync(loadout, profile.BankBalance);

                    var listed = catalog.Groups.ToDictionary(g => g.Id.Id);
                    Assert.That(listed, Has.Count.EqualTo(catalog.Groups.Count), $"{role.ID} lists a group twice");

                    foreach (var groupId in role.Groups)
                    {
                        if (!proto.TryIndex(groupId, out var groupProto) || groupProto.Hidden)
                        {
                            Assert.That(listed.ContainsKey(groupId.Id), Is.False, $"{role.ID}: {groupId} is hidden");
                            continue;
                        }

                        var expected = groupProto.Loadouts
                            .Concat(groupProto.Subgroups.Where(s => proto.HasIndex(s)).SelectMany(s => proto.Index(s).Loadouts))
                            .Distinct()
                            .Where(id => proto.HasIndex(id) && !loadout.IsHidden(profile, session, id, collection))
                            .Select(id => id.Id)
                            .ToList();

                        Assert.That(listed.ContainsKey(groupId.Id), Is.EqualTo(expected.Count > 0), $"{role.ID}: {groupId}");
                        if (!listed.TryGetValue(groupId.Id, out var group))
                            continue;

                        Assert.That(group.Entries.Select(e => e.Id.Id), Is.EquivalentTo(expected), $"{role.ID}: {groupId}");
                        Assert.That(group.Entries.Select(e => e.Name), Has.None.Empty, $"{role.ID}: {groupId} has a nameless entry");
                        Assert.That(group.Label, Is.Not.Empty.And.Not.StartWith("loadout-group"), $"{role.ID}: {groupId}");

                        // Everything the defaults picked is listed, so no row starts out blank.
                        Assert.That(group.Stale, Is.Zero, $"{role.ID}: {groupId} has default picks the window doesn't list");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A species with no feet has no shoes to list, so the shoes group is left out rather than shown empty.</summary>
    [Test]
    public async Task GroupWithNothingToOfferIsLeftOut()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;

        await client.WaitAssertion(() =>
        {
            var role = proto.Index<RoleLoadoutPrototype>(Role);
            List<string> Groups(string species)
            {
                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species);
                var loadout = new RoleLoadout(role.ID);
                loadout.SetDefault(profile, session, proto);
                return new LoadoutCatalog(profile, loadout, role, session, collection).Groups.Select(g => g.Id.Id).ToList();
            }

            Assert.That(Groups("Human"), Does.Contain("MercenaryShoes"));
            Assert.That(Groups("ProtoDionae"), Does.Not.Contain("MercenaryShoes"));
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>A search finds items by the name of their group too, and lists name matches before description matches.</summary>
    [Test]
    public async Task SearchRanksNamesBeforeDescriptions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            var role = proto.Index<RoleLoadoutPrototype>(Role);
            var loadout = new RoleLoadout(role.ID);
            loadout.SetDefault(profile, session, proto);
            var catalog = new LoadoutCatalog(profile, loadout, role, session, collection);

            var results = catalog.Search("gun");
            Assert.That(results, Is.Not.Empty);
            Assert.That(results.All(e => e.SearchText.Contains("gun")), Is.True);

            // Name matches you can take, name matches you can't, then the same for description-only matches.
            var ranks = results.Select(e => (e.NameText.Contains("gun") ? 0 : 2) + (e.Locked ? 1 : 0)).ToList();
            Assert.That(ranks, Is.Ordered);
            Assert.That(ranks[0], Is.Zero, "something named after a gun can be taken");

            var weapons = catalog.Groups.First(g => g.Category == LoadoutCategory.Weapons);
            var byGroup = catalog.Search(weapons.Label);
            Assert.That(byGroup.Where(e => e.Group == weapons).Select(e => e.Id), Is.EquivalentTo(weapons.Entries.Select(e => e.Id)),
                "a group's name finds everything in the group");

            Assert.That(catalog.Search("zzzz no such thing"), Is.Empty);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The catalog's cost and its "can't afford" flags follow the balance the way the server pays at spawn, and a
    /// required group whose pick is dropped names the fallback issued in its place.
    /// </summary>
    [Test]
    public async Task UnaffordablePicksAreFlaggedWithTheirFallback()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            var role = proto.Index<RoleLoadoutPrototype>(Role);
            var loadout = new RoleLoadout(role.ID);
            loadout.SetDefault(profile, session, proto);
            var catalog = new LoadoutCatalog(profile, loadout, role, session, collection);

            long Spend() => loadout.SelectedLoadouts.Values.SelectMany(l => l).Sum(l => (long) System.Math.Max(0, proto.Index(l.Prototype).Price));

            // A priced item in a required single-pick group that has a free fallback to drop back to.
            var group = catalog.Groups.First(g => g.Single && !g.Optional && g.Proto.Fallbacks.Count > 0 &&
                g.Entries.Any(e => !e.Locked && e.Price > 0) &&
                g.Proto.Fallbacks.Any(f => proto.Index(f).Price <= 0));
            var priced = group.Entries.First(e => !e.Locked && e.Price > 0);
            loadout.AddLoadout(group.Id, priced.Id, proto);

            catalog.Sync(loadout, long.MaxValue);
            Assert.That(catalog.Cost, Is.EqualTo(Spend()), "the cost is the price of everything selected");
            Assert.That(catalog.Groups.Sum(g => g.UnaffordableCount), Is.Zero, "with money to spare nothing is flagged");
            Assert.That(priced.Selected, Is.True);

            // One short of what the selection costs, and nothing else priced before it: the item itself is dropped.
            catalog.Sync(loadout, priced.Price - 1);
            Assert.That(priced.Unaffordable, Is.True, "the pick the balance can't cover is flagged");
            Assert.That(group.UnaffordableCount, Is.EqualTo(1));
            Assert.That(group.Replacement, Is.Not.Null.And.Not.Empty, "a required group names what is issued instead");
            Assert.That(catalog.Cost, Is.EqualTo(Spend()), "the cost still counts what was asked for");

            catalog.Sync(loadout, long.MaxValue);
            Assert.That(priced.Unaffordable, Is.False, "the flag clears once the balance covers it");
            Assert.That(group.Replacement, Is.Null);
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>Items that share a name within a group are told apart, so a group never lists the same name twice.</summary>
    [Test]
    public async Task SameNamedItemsAreToldApart()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;

        await client.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");
            var role = proto.Index<RoleLoadoutPrototype>(Role);
            var loadout = new RoleLoadout(role.ID);
            loadout.SetDefault(profile, session, proto);
            var catalog = new LoadoutCatalog(profile, loadout, role, session, collection);

            // Every headset entity inherits the name "headset".
            var ears = catalog.Groups.First(g => g.Id == "MercenaryEars");
            var raw = ears.Entries.Select(e => proto.Index<EntityPrototype>(e.Icon!.Value).Name).ToList();
            Assert.That(raw.Distinct().Count(), Is.LessThan(raw.Count), "the group no longer has same-named items; pick another");

            var names = ears.Entries.Select(e => e.Name).ToList();
            Assert.That(names, Is.Unique);
            Assert.That(names, Has.All.Contain(raw[0]), "the shared name is kept");
        });

        await pair.CleanReturnAsync();
    }

    /// <summary>
    /// The window opens for every role, lists one rail row per group and deletes its preview entities both when
    /// closed and when disposed, the two ways the editor gets rid of it.
    /// </summary>
    [Test]
    public async Task WindowOpensForEveryRole()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        var proto = client.ResolveDependency<IPrototypeManager>();
        var entMan = client.ResolveDependency<IEntityManager>();
        var collection = client.ResolveDependency<IDependencyCollection>();
        var session = client.ResolveDependency<IPlayerManager>().LocalSession!;
        var profile = HumanoidCharacterProfile.DefaultWithSpecies("Human");

        var roles = new List<RoleLoadoutPrototype>();
        await client.WaitPost(() => roles.AddRange(proto.EnumeratePrototypes<RoleLoadoutPrototype>().OrderBy(r => r.ID)));

        for (var i = 0; i < roles.Count; i++)
        {
            var role = roles[i];
            var close = i % 2 == 0;
            WolfgateLoadoutWindow window = default!;
            var before = 0;

            await client.WaitPost(() =>
            {
                before = entMan.EntityCount;
                var loadout = new RoleLoadout(role.ID);
                loadout.SetDefault(profile, session, proto);
                window = new WolfgateLoadoutWindow(profile, loadout, role, session, collection);
                window.RefreshLoadouts(loadout, session, collection);
                window.OpenCenteredLeft();
            });
            await pair.RunTicksSync(4);

            await client.WaitAssertion(() =>
            {
                var groups = window.Catalog.Groups;
                Assert.Multiple(() =>
                {
                    Assert.That(window.IsOpen, Is.True, role.ID);
                    Assert.That(Descendants(window).OfType<LoadoutRailRow>().Count(), Is.EqualTo(groups.Count), role.ID);

                    if (groups.Count > 0)
                    {
                        Assert.That(window.OpenedGroup, Is.Not.Null, role.ID);
                        Assert.That(Descendants(window).OfType<LoadoutTile>().Count(t => t.Visible), Is.GreaterThan(0), $"{role.ID} shows no tile");
                        Assert.That(window.IconCount, Is.GreaterThan(0).And.LessThan(200), $"{role.ID} preview entities");
                    }
                });

                if (close)
                {
                    window.Close();
                    Assert.That(entMan.EntityCount, Is.EqualTo(before), $"{role.ID} left preview entities behind on close");
                }

                window.Dispose();
                Assert.That(entMan.EntityCount, Is.EqualTo(before), $"{role.ID} left preview entities behind");
            });
        }

        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
