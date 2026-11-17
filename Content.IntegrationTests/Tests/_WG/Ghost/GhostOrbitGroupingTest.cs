#nullable enable
using System.Linq;
using Content.Server._WG.Ghost;
using Content.Server.Nuke;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Mono.Company;
using Content.Shared._WG.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nuke;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._WG.Ghost;

/// <summary>
/// The ghost menu's targets carry the faction and place its two views group by, and the nuke and its disk are only
/// listed while something is happening with them.
/// </summary>
[TestFixture]
[TestOf(typeof(GhostOrbitSystem))]
public sealed class GhostOrbitGroupingTest
{
    [Test]
    public async Task TargetsCarryFactionAndPlace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var orbit = entities.System<GhostOrbitSystem>();

        await server.WaitAssertion(() =>
        {
            var federal = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.EnsureComponent<CompanyComponent>(federal).CompanyName = "TSFCivilian";
            var nobody = entities.SpawnEntity("MobHuman", map.GridCoords);

            var targets = orbit.BuildTargets(EntityUid.Invalid, isAdmin: true);
            var fed = targets.Single(t => t.Entity == entities.GetNetEntity(federal));
            var none = targets.Single(t => t.Entity == entities.GetNetEntity(nobody));

            Assert.That(fed.Faction, Is.EqualTo("Trans-Solarian Federation"),
                "The Federation's civilian arm is listed under the Federation.");
            Assert.That(fed.FactionColor, Is.Not.Null);
            Assert.That(none.Faction, Is.Null, "A mob with no company has no faction.");
            Assert.That(fed.Place, Is.Not.Null.And.Not.Empty, "Every target says where it is.");
            Assert.That(fed.Place, Is.EqualTo(none.Place), "Two mobs on one map share a place.");
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task NukeAndDiskOnlyWhenSomethingIsHappening()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.EntMan;
        var orbit = entities.System<GhostOrbitSystem>();

        await server.WaitAssertion(() =>
        {
            bool Listed(EntityUid uid) => orbit.BuildTargets(EntityUid.Invalid, isAdmin: true)
                .Any(t => t.Entity == entities.GetNetEntity(uid) && t.Category == GhostOrbitCategory.Interest);

            var nuke = entities.SpawnEntity("NuclearBomb", map.GridCoords);
            Assert.That(Listed(nuke), Is.False, "A nuke sitting idle is not listed.");
            var nukes = entities.System<NukeSystem>();
            nukes.ArmBomb(nuke);
            Assert.That(entities.GetComponent<NukeComponent>(nuke).Status, Is.EqualTo(NukeStatus.ARMED));
            Assert.That(Listed(nuke), Is.True, "An armed nuke is.");
            nukes.DisarmBomb(nuke);

            var disk = entities.SpawnEntity("NukeDisk", map.GridCoords);
            Assert.That(Listed(disk), Is.True, "A disk lying loose is listed.");

            var crate = entities.SpawnEntity("CrateGenericSteel", map.GridCoords);
            Assert.That(entities.System<EntityStorageSystem>().Insert(disk, crate), Is.True);
            Assert.That(Listed(disk), Is.False, "A disk stowed away is not.");

            entities.System<EntityStorageSystem>().Remove(disk, crate);
            var carrier = entities.SpawnEntity("MobHuman", map.GridCoords);
            Assert.That(entities.System<SharedHandsSystem>().TryPickupAnyHand(carrier, disk), Is.True);
            Assert.That(Listed(disk), Is.True, "A disk someone carries is listed.");
        });

        await pair.CleanReturnAsync();
    }
}
